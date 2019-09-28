using System;
using System.Windows;
using System.Collections.Generic;
using System.Linq;
using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Threading;
using PeterO.Cbor;

namespace CTAPBLETool
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {

        private BluetoothLEAdvertisementWatcher AdvWatcher;
        private BluetoothLEDevice BleDevice;
        private GattDeviceService Service_Fido;
        private GattCharacteristic Characteristic_Send;
        private GattCharacteristic Characteristic_Receive;
        private List<byte> ReceveData;

        public MainWindow()
        {
            InitializeComponent();

            addLog("Scanから始めてください");
            addLog("");

        }

        private void ButtonScan_Click(object sender, RoutedEventArgs e)
        {
            this.AdvWatcher = new BluetoothLEAdvertisementWatcher();

            // インターバルがゼロのままだと、CPU負荷が高くなりますので、適切な間隔(SDK サンプルでは 1秒)に指定しないと、アプリの動作に支障をきたすことになります。
            this.AdvWatcher.SignalStrengthFilter.SamplingInterval = TimeSpan.FromMilliseconds(1000);

            // rssi >= -60のときスキャンする
            //this.advWatcher.SignalStrengthFilter.InRangeThresholdInDBm = -60;

            // パッシブスキャン/アクティブスキャン
            // スキャン応答のアドバタイズを併せて受信する場合＝BluetoothLEScanningMode.Active
            // ActiveにするとBluetoothLEAdvertisementType.ScanResponseが取れるようになる。（スキャンレスポンスとは追加情報のこと）
            // ※電力消費量が大きくなり、またバックグラウンド モードでは使用できなくなるらしい
            //this.advWatcher.ScanningMode = BluetoothLEScanningMode.Active;
            this.AdvWatcher.ScanningMode = BluetoothLEScanningMode.Passive;

            // アドバタイズパケットの受信イベント
            this.AdvWatcher.Received += this.Watcher_Received;

            // スキャン開始
            this.AdvWatcher.Start();

            addLog("Scan開始しました.BLE FIDOキーをONにしてください");
            addLog("");
        }

        private async void Watcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            await this.Dispatcher.InvokeAsync(() => {
                this.CheckArgs(args);
            });
        }

        public async void CheckArgs(BluetoothLEAdvertisementReceivedEventArgs args)
        {
            Console.WriteLine("★Scan");

            // console log
            DebugMethods.OutputLog(args);

            // FIDOサービスを検索
            var fidoServiceUuid = new Guid("0000fffd-0000-1000-8000-00805f9b34fb");
            if (args.Advertisement.ServiceUuids.Contains(fidoServiceUuid) == false) {
                return;
            }

            // 発見
            addLog("Scan FIDO Device");
            this.AdvWatcher.Stop();

            // connect
            {
                addLog("Conncect FIDO Device");
                BleDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
                DebugMethods.OutputLog(BleDevice);
            }

            // FIDOのサービスをGET
            {
                addLog("Connect FIDO Service");
                var services = await BleDevice.GetGattServicesForUuidAsync(fidoServiceUuid);
                if (services.Services.Count <= 0) {
                    // サービス無し
                    addLog("Error Connect FIDO Service");
                    return;
                }
                Service_Fido = services.Services.First();
            }

            // Characteristicアクセス
            // - コマンド送信ハンドラ設定
            // - 応答受信ハンドラ設定
            {
                // FIDO Service Revision(Read)
                await DebugMethods.OutputLog(Service_Fido, GattCharacteristicUuids.SoftwareRevisionString);

                // FIDO Control Point Length(Read-2byte)
                await DebugMethods.OutputLog(Service_Fido, new Guid("F1D0FFF3-DEAA-ECEE-B42F-C9BA7ED623BB"));

                // FIDO Service Revision Bitfield(Read/Write-1+byte)
                await DebugMethods.OutputLog(Service_Fido, new Guid("F1D0FFF4-DEAA-ECEE-B42F-C9BA7ED623BB"));

                // FIDO Status(Notiry) 受信データ
                {
                    var characteristics = await Service_Fido.GetCharacteristicsForUuidAsync(new Guid("F1D0FFF2-DEAA-ECEE-B42F-C9BA7ED623BB"));
                    if (characteristics.Characteristics.Count > 0) {
                        this.Characteristic_Receive = characteristics.Characteristics.First();
                        if (this.Characteristic_Receive == null) {
                            Console.WriteLine("Characteristicに接続できない...");
                        } else {
                            if (this.Characteristic_Receive.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)) {
                                // イベントハンドラ追加
                                this.Characteristic_Receive.ValueChanged += characteristicChanged_OnReceiveFromDevice;

                                // これで有効になる
                                await this.Characteristic_Receive.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                            }
                        }
                    }
                }

                // FIDO Control Point(Write) 送信データ
                {
                    var characteristics = await Service_Fido.GetCharacteristicsForUuidAsync(new Guid("F1D0FFF1-DEAA-ECEE-B42F-C9BA7ED623BB"));
                    if (characteristics.Characteristics.Count > 0) {
                        this.Characteristic_Send = characteristics.Characteristics.First();
                        if (this.Characteristic_Send == null) {
                            Console.WriteLine("Characteristicに接続できない...");
                        }
                    }
                }

                addLog("BLE FIDOキーと接続しました!");
                addLog("");
            }
        }

        private async Task<bool> sendCommand(byte[] command)
        {
            bool ret = false;
            try {
                if (command == null) {
                    return (ret);
                }

                // log
                addLog($"send Command...");
                addLog($"{BitConverter.ToString(command)}");

                var result = await Characteristic_Send.WriteValueAsync(command.AsBuffer(), GattWriteOption.WriteWithResponse);
                if (result != GattCommunicationStatus.Success) {
                    // error
                    return (false);
                }

                ReceveData = new List<byte>();

            } catch (Exception ex) {
                addLog($"Exception...{ex.Message})");
            }
            return (ret);
        }

        protected void characteristicChanged_OnReceiveFromDevice(GattCharacteristic sender, GattValueChangedEventArgs eventArgs)
        {
            addLog($"characteristicChanged...");
            addLog($"- Length={eventArgs.CharacteristicValue.Length}");
            if (eventArgs.CharacteristicValue.Length <= 0) {
                return;
            }

            byte[] data = new byte[eventArgs.CharacteristicValue.Length];
            Windows.Storage.Streams.DataReader.FromBuffer(eventArgs.CharacteristicValue).ReadBytes(data);

            // for log
            {
                var tmp = BitConverter.ToString(data);
                addLog($"- Data...");
                addLog($"{tmp}");
            }

            // parse
            {
                // [0] STAT
                if (data[0] == 0x81) {
                    addLog($"PING");
                } else if (data[0] == 0x82) {
                    addLog($"KEEPALIVE");
                } else if (data[0] == 0x83) {
                    addLog($"MSG");
                    // [1] HLEN
                    // [2] LLEN
                    // [3-] DATA
                    var buff = data.Skip(3).Take(data.Length).ToArray();
                    // 最初の1byteは応答ステータスで2byteからCBORデータ
                    var cbor = buff.Skip(1).Take(buff.Length).ToArray();
                    // 受信バッファに追加
                    ReceveData.AddRange(cbor.ToList());

                } else if (data[0] == 0xbe) {
                    // CANCEL
                    addLog($"CANCEL");
                } else if (data[0] == 0xbf) {
                    // ERROR
                    addLog($"ERROR");
                } else {
                    // データの続き
                    addLog($"CBOR Data...");
                    var buff = data;
                    // 最初の1byteは応答ステータスで2byteからCBORデータ
                    var cbor = buff.Skip(1).Take(buff.Length).ToArray();
                    // 受信バッファに追加
                    ReceveData.AddRange(cbor.ToList());
                }
            }

            addLog("受信しました");
            addLog("");
            return;
        }

        private void ButtonDiscon_Click(object sender, RoutedEventArgs e)
        {
            if (Service_Fido != null) {
                Service_Fido.Dispose();
                addLog("FIDO Service Disposed");
            }

            if (BleDevice != null) {
                BleDevice.Dispose();
                addLog("BLE Device Disposed");
            }
            addLog("BLE FIDOキーと切断しました");
            addLog("");
        }

        private void addLog(string message)
        {
            Console.WriteLine($"{message}");
            var ignored = this.Dispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)(() => {
                textLog.Text += message + Environment.NewLine;
            }));
        }

        private async void ButtonGetInfo_Click(object sender, RoutedEventArgs e)
        {
            try {
                var cmd = new byte[4];

                // Command identifier
                cmd[0] = 0x83;      // MSG

                // High part of data length
                cmd[1] = 0x00;

                // Low part of data length
                cmd[2] = 0x01;

                // Data (s is equal to the length)
                cmd[3] = 0x04;

                var result = await sendCommand(cmd);

                addLog("送信しました");
                addLog("");

            } catch (Exception ex) {
                Console.WriteLine($"Exception...{ex.Message})");
            }
        }

        private async void ButtonMakeCredential_Click(object sender, RoutedEventArgs e)
        {
            try {
                // param
                byte[] ClientDataHash = System.Text.Encoding.ASCII.GetBytes("this is challenge");
                string RpId = "gebogebo.com";
                string RpName = "geborp";
                string UserId = "12345678";
                string UserName = "gebo";
                string UserDisplayName = "gebo";
                bool Option_rk = false;
                bool Option_uv = true;

                var cbor = CBORObject.NewMap();

                // 0x01 : clientDataHash
                cbor.Add(0x01, ClientDataHash);

                // 0x02 : rp
                cbor.Add(0x02, CBORObject.NewMap().Add("id", RpId).Add("name", RpName));

                // 0x03 : user
                {
                    var user = CBORObject.NewMap();
                    user.Add("id", System.Text.Encoding.ASCII.GetBytes(UserId));
                    user.Add("name", UserName);
                    user.Add("displayName", UserDisplayName);

                    cbor.Add(0x03, user);
                }

                // 0x04 : pubKeyCredParams
                {
                    var pubKeyCredParams = CBORObject.NewMap();
                    pubKeyCredParams.Add("alg", -7);
                    pubKeyCredParams.Add("type", "public-key");
                    cbor.Add(0x04, CBORObject.NewArray().Add(pubKeyCredParams));
                }

                // 0x07 : options
                {
                    var opt = CBORObject.NewMap();
                    opt.Add("rk", Option_rk);
                    opt.Add("uv", Option_uv);
                    cbor.Add(0x07, opt);
                }

                /*
                if (PinAuth != null) {
                    // pinAuth(0x08)
                    cbor.Add(0x08, PinAuth);

                    // 0x09:pinProtocol
                    cbor.Add(0x09, 1);
                }
                */

                var payloadb = cbor.EncodeToBytes();

                var cmd = new List<byte>();

                // Command identifier
                cmd.Add(0x83);      // MSG

                // High part of data length
                cmd.Add(0x00);

                // Low part of data length
                cmd.Add((byte)(payloadb.Length + 1));

                // Data (s is equal to the length)
                cmd.Add(0x01);          // authenticatorMakeCredential (0x01)
                cmd.AddRange(payloadb);
                var result = await sendCommand(cmd.ToArray());

            } catch (Exception ex) {
                Console.WriteLine($"Exception...{ex.Message})");
            }
        }

        private void ButtonReceiveData_Click(object sender, RoutedEventArgs e)
        {
            try {
                addLog("< Receive Data >");

                var tmp = BitConverter.ToString(this.ReceveData.ToArray());
                addLog(tmp);
                addLog("");

                addLog("< Parse CBOR >");
                var cbor = CBORObject.DecodeFromBytes(this.ReceveData.ToArray(), CBOREncodeOptions.Default);
                addLog(cbor.ToJSONString());
                addLog("");

                bool isAttestation = false;
                {
                    foreach (var key in cbor.Keys) {
                        var keyVal = key.AsByte();
                        if (keyVal == 0x01) {
                            // fmt
                            var fmt = cbor[key].AsString();
                            if (fmt == "packed") {
                                // Attestation <- authenticatorMakeCredentialの応答
                                isAttestation = true;
                                break;
                            }
                        }
                    }
                }
                if (isAttestation == true) {
                    foreach (var key in cbor.Keys) {
                        var keyVal = key.AsByte();
                        if (keyVal == 0x02) {
                            // authData
                            parseAuthData(cbor[key].GetByteString());
                        }
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine($"Exception...{ex.Message})");
            }
        }

        private void parseAuthData(byte[] data)
        {
            int index = 0;

            // rpIdHash	(32)
            byte[] RpIdHash = data.Skip(index).Take(32).ToArray();
            index = index + 32;

            // flags(1)
            {
                byte flags = data[index];
                index++;
                bool Flags_UserPresentResult = Common.GetBit(flags, 0);
                bool Flags_UserVerifiedResult = Common.GetBit(flags, 2);
                bool Flags_AttestedCredentialDataIncluded = Common.GetBit(flags, 6);
                bool Flags_ExtensionDataIncluded = Common.GetBit(flags, 7);
            }

            // signCount(4)
            {
                //int SignCount = Common.ToInt32(data, index, true);
                index = index + 4;
            }

            // aaguid	16
            byte[] Aaguid = data.Skip(index).Take(16).ToArray();
            index = index + 16;

            // credentialId
            {
                int credentialIdLength = Common.ToInt16(data, index, true);
                index = index + 2;

                byte[] CredentialId = data.Skip(index).Take(credentialIdLength).ToArray();

                addLog($"CredentialId = {Common.BytesToHexString(CredentialId)}");

                addLog("");

                index = index + credentialIdLength;
            }

            // credentialPublicKey
            {
                byte[] CredentialPublicKeyByte = data.Skip(index).ToArray();
                var credentialPublicKeyCobr = CBORObject.DecodeFromBytes(CredentialPublicKeyByte, CBOREncodeOptions.Default);
                string CredentialPublicKey = credentialPublicKeyCobr.ToJSONString();
                Console.WriteLine("credentialPublicKeyCobr:" + CredentialPublicKey);
            }

        }

        private async void ButtonGetAssertion_Click(object sender, RoutedEventArgs e)
        {
            try {
                // param
                string RpId = "gebogebo.com";
                byte[] ClientDataHash = System.Text.Encoding.ASCII.GetBytes("this is challenge");
                byte[] AllowList_CredentialId = Common.HexStringToBytes("D2A464B2FDFB219245ED5C1E81FCEC8452915B3DB13BE0D608691F51909A2136331CE8663803E23A6B7B895F38B98B70A8165578391C571B45EF15EEF7282D36617CAA36931CBE6DF69A8166F18EB1ED0634B3D0055C186C794AF355464FE8A6");
                bool Option_up = true;
                bool Option_uv = true;

                var cbor = CBORObject.NewMap();

                // 0x01 : rpid
                cbor.Add(0x01, RpId);

                // 0x02 : clientDataHash
                cbor.Add(0x02, ClientDataHash);

                // 0x03 : allowList
                if (AllowList_CredentialId != null) {
                    var pubKeyCredParams = CBORObject.NewMap();
                    pubKeyCredParams.Add("type", "public-key");
                    pubKeyCredParams.Add("id", AllowList_CredentialId);
                    cbor.Add(0x03, CBORObject.NewArray().Add(pubKeyCredParams));
                }

                // 0x05 : options
                {
                    var opt = CBORObject.NewMap();
                    opt.Add("up", Option_up);
                    opt.Add("uv", Option_uv);
                    cbor.Add(0x05, opt);
                }

                /*
                if (PinAuth != null) {
                    // pinAuth(0x06)
                    cbor.Add(0x06, PinAuth);
                    // 0x07:pinProtocol
                    cbor.Add(0x07, 1);
                }
                */

                var payloadb = cbor.EncodeToBytes();

                var cmd = new List<byte>();

                // Command identifier
                cmd.Add(0x83);      // MSG

                // High part of data length
                cmd.Add(0x00);

                // Low part of data length
                cmd.Add((byte)(payloadb.Length + 1));

                // パケット2つに分割送信してみる
                // fidoControlPointLength=0x9B(155byte)
                // なので、1パケット155になるように分割する
                // ※155より小さい値で分割してもエラーになる
                // ※このサンプルでは固定値にしていますが、fidoControlPointLengthが155とは限らないので注意
                var send1 = payloadb.Skip(0).Take(151).ToArray();
                var send2 = payloadb.Skip(151).Take(100).ToArray();

                // Frame 0
                cmd.Add(0x02);          // authenticatorGetAssertion (0x02)
                cmd.AddRange(send1);
                var result1 = await sendCommand(cmd.ToArray());

                // Frame 1
                cmd.Clear();
                cmd.Add(0x00);
                cmd.AddRange(send2);
                var result2 = await sendCommand(cmd.ToArray());

                /*
                // Data (s is equal to the length)
                cmd.Add(0x02);          // authenticatorGetAssertion (0x02)
                cmd.AddRange(payloadb);

                var result = await sendCommand(cmd.ToArray());
                */

            } catch (Exception ex) {
                Console.WriteLine($"Exception...{ex.Message})");
            }
        }

    }
}

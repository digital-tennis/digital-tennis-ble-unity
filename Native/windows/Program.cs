using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

internal sealed class BleWindow:Form
{
    static readonly Guid Service=new("6d7c1000-9d40-4cb7-a381-5e908be0a001"),Tx=new("6d7c1001-9d40-4cb7-a381-5e908be0a001"),Rx=new("6d7c1002-9d40-4cb7-a381-5e908be0a001");
    readonly string[] args;BluetoothLEAdvertisementWatcher? watcher;BluetoothLEDevice? device;GattDeviceService? service;GattSession? session;GattCharacteristic? command;
    static void Emit(object value){Console.WriteLine(JsonSerializer.Serialize(value));Console.Out.Flush();}
    public BleWindow(string[] args){this.args=args;Opacity=0;ShowInTaskbar=false;Width=1;Height=1;Shown+=async(_,_)=>{Hide();await Start();};FormClosed+=(_,_)=>{watcher?.Stop();session?.Dispose();service?.Dispose();device?.Dispose();};}
    async Task Start()
    {
        try
        {
            if(args.Length==0)throw new InvalidDataException();
            if(args[0]=="scan")
            {
                watcher=new BluetoothLEAdvertisementWatcher{ScanningMode=BluetoothLEScanningMode.Active};watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(Service);
                watcher.Received+=(_,e)=>{if(e.Advertisement.ServiceUuids.Contains(Service))Emit(new{kind="device",id=e.BluetoothAddress.ToString("X12"),name=string.IsNullOrWhiteSpace(e.Advertisement.LocalName)?"Digital Tennis Racket":e.Advertisement.LocalName});};
                watcher.Stopped+=(_,e)=>{if(e.Error!=BluetoothError.Success)Emit(new{kind="unavailable",status="Bluetooth 전원·사용 권한을 확인해 주세요."});};watcher.Start();
            }
            else if(args[0]=="connect"&&args.Length==2&&ulong.TryParse(args[1],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var address))
            {
                // Runs on this STA Windows Forms UI thread, as required for OS consent.
                device=await BluetoothLEDevice.FromBluetoothAddressAsync(address);if(device==null)throw new IOException();
                Emit(new{kind="pairing"});
                if(!device.DeviceInformation.Pairing.IsPaired){var pairing=await device.DeviceInformation.Pairing.PairAsync(DevicePairingProtectionLevel.Encryption);if(pairing.Status!=DevicePairingResultStatus.Paired&&pairing.Status!=DevicePairingResultStatus.AlreadyPaired)throw new IOException();}
                session=await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId);session.MaintainConnection=true;
                var services=await device.GetGattServicesForUuidAsync(Service,BluetoothCacheMode.Uncached);if(services.Status!=GattCommunicationStatus.Success||services.Services.Count!=1)throw new IOException();service=services.Services[0];
                var values=await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);if(values.Status!=GattCommunicationStatus.Success)throw new IOException();
                var notify=values.Characteristics.FirstOrDefault(x=>x.Uuid==Tx&&x.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify));command=values.Characteristics.FirstOrDefault(x=>x.Uuid==Rx&&x.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write));if(notify==null||command==null)throw new IOException();
                notify.ValueChanged+=(_,e)=>{using var reader=DataReader.FromBuffer(e.CharacteristicValue);if(reader.UnconsumedBufferLength!=20)return;byte[] b=new byte[20];reader.ReadBytes(b);Emit(new{kind="packet",hex=Convert.ToHexString(b)});};
                if(await notify.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify)!=GattCommunicationStatus.Success)throw new IOException();
                device.ConnectionStatusChanged+=(_,_)=>{if(device.ConnectionStatus==BluetoothConnectionStatus.Disconnected)Emit(new{kind="disconnected"});};Emit(new{kind="connected",id=args[1]});
            }
            else throw new InvalidDataException();
            _=Task.Run(()=>{string? line;while((line=Console.ReadLine())!=null){string copy=line;BeginInvoke((Action)(async()=>await Write(copy)));}BeginInvoke((Action)(()=>Close()));});
        }
        catch(Exception){Emit(new{kind="error",status="Bluetooth 연결·서비스·페어링을 확인하지 못했습니다."});Close();}
    }
    async Task Write(string hex)
    {
        if(command==null||hex.Length!=40)return;
        try{using var writer=new DataWriter();writer.WriteBytes(Convert.FromHexString(hex));var result=await command.WriteValueWithResultAsync(writer.DetachBuffer(),GattWriteOption.WriteWithResponse);if(result.Status!=GattCommunicationStatus.Success)throw new IOException();}
        catch(Exception){Emit(new{kind="error",status="보정 명령을 보내지 못했습니다."});}
    }
    [STAThread] static void Main(string[] args){Application.EnableVisualStyles();Application.Run(new BleWindow(args));}
}

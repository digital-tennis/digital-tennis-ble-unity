using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace DigitalTennis.Controller
{
    // A bundled, platform-native helper owns OS Bluetooth access. Each discovery
    // or connection has a fresh process/immutable operation, so late callbacks
    // from a cancelled worker can never acquire the next connection's identity.
    public sealed class NativeBleProcessTransport:IBleTransport
    {
        [DataContract] private sealed class Message
        {
            [DataMember(Name="kind")]public string Kind {get;set;}
            [DataMember(Name="id")]public string Id {get;set;}
            [DataMember(Name="name")]public string Name {get;set;}
            [DataMember(Name="status")]public string Status {get;set;}
            [DataMember(Name="hex")]public string Hex {get;set;}
        }
        private readonly string executable;private readonly Func<double> clock;private readonly Action<string> diagnostic;private Process worker;private bool disposed;private ulong activeOperation;
        public bool Available=>!disposed&&File.Exists(executable);
        public event Action<BleTransportEvent> Received;
        public NativeBleProcessTransport(string executable,Func<double> clock,Action<string> diagnostic=null){this.diagnostic=diagnostic;this.executable=executable??throw new ArgumentNullException(nameof(executable));this.clock=clock??throw new ArgumentNullException(nameof(clock));}
        public void Scan(ulong operation)=>Start("scan",null,operation);
        public void Connect(string id,ulong operation)=>Start("connect",id,operation);
        private void Start(string mode,string id,ulong operation)
        {
            Disconnect();activeOperation=operation;if(!Available){Emit(operation,"unavailable","Bluetooth 실행 모듈을 찾을 수 없습니다.");return;}
            if(id!=null&&(id.Length>256||id.IndexOfAny(new[]{'"','\r','\n','\0'})>=0)){Emit(operation,"error","기기 식별자가 올바르지 않습니다.");return;}
            try
            {
                var process=new Process{StartInfo=new ProcessStartInfo(executable,mode+(id==null?"":" \""+id+"\"")){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,CreateNoWindow=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8},EnableRaisingEvents=true};
                process.OutputDataReceived+=(sender,args)=>Read(operation,args.Data);
                // Never surface arbitrary native stderr (could contain OS identifiers/tokens).
                process.ErrorDataReceived+=(sender,args)=>{};
                process.Exited+=(sender,args)=>{if(!disposed&&ReferenceEquals(worker,process))Emit(operation,"disconnected",null);};
                worker=process;if(!process.Start())throw new IOException("Could not start BLE helper");process.BeginOutputReadLine();process.BeginErrorReadLine();
            }
            catch(Exception e) when(e is IOException||e is System.ComponentModel.Win32Exception||e is InvalidOperationException){Disconnect();Emit(operation,"error","Bluetooth 모듈을 실행할 수 없습니다. 설치 상태를 확인해 주세요.");}
        }
        private void Emit(ulong operation,string kind,string status)=>Received?.Invoke(new BleTransportEvent{Operation=operation,Kind=kind,Status=status,ReceivedAt=clock()});
        private void Read(ulong operation,string line)
        {
            if(disposed||string.IsNullOrEmpty(line)||line.Length>2048)return;
            diagnostic?.Invoke("native-line-length="+line.Length);
            try
            {
                Message message;using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(line)))message=(Message)new DataContractJsonSerializer(typeof(Message)).ReadObject(stream);
                diagnostic?.Invoke("native-event="+(message.Kind=="device"||message.Kind=="pairing"||message.Kind=="security"||message.Kind=="connected"||message.Kind=="packet"||message.Kind=="error"||message.Kind=="unavailable"?message.Kind:"other"));
                byte[] packet=null;if(message.Hex!=null){int size=message.Kind=="security"?4:20;if(message.Hex.Length!=size*2)return;packet=new byte[size];for(int i=0;i<size;i++)packet[i]=Convert.ToByte(message.Hex.Substring(i*2,2),16);}
                Received?.Invoke(new BleTransportEvent{Operation=operation,Kind=message.Kind,Id=message.Id,Name=message.Name,Status=message.Status,Packet=packet,ReceivedAt=clock()});
            }
            catch(Exception e) when(e is SerializationException||e is FormatException||e is OverflowException||e is ArgumentException) { diagnostic?.Invoke("native-decode-failed="+e.GetType().Name); }
        }
        public void Write(byte[] packet,ulong operation)
        {
            if(disposed||operation!=activeOperation||worker==null||packet==null||packet.Length!=20)return;
            try{worker.StandardInput.WriteLine(BitConverter.ToString(packet).Replace("-",""));worker.StandardInput.Flush();}
            catch(Exception e) when(e is IOException||e is InvalidOperationException||e is ObjectDisposedException){Emit(operation,"error","기기 명령을 보내지 못했습니다.");}
        }
        public void Disconnect()
        {
            var old=worker;worker=null;if(old==null)return;
            try{old.StandardInput.Close();if(!old.HasExited)old.Kill();}catch(Exception e) when(e is InvalidOperationException||e is System.ComponentModel.Win32Exception||e is IOException){}finally{old.Dispose();}
        }
        public void Dispose(){if(disposed)return;disposed=true;Disconnect();}
    }
}

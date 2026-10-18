using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using DigitalTennis.Protocol;

namespace DigitalTennis.Controller
{
    public enum BleLinkState { Unavailable,Disconnected,Scanning,Connecting,Pairing,Connected,Error }
    public readonly struct BleDevice { public readonly string Id,Name;public BleDevice(string id,string name){Id=id;Name=name;} }
    public readonly struct RacketIntent
    {
        public readonly bool UseGameShotSettings;public readonly SwingKind Kind;public readonly uint Sequence;public readonly ulong Epoch;public readonly double ReceivedAt;public readonly float Power,AimX,AimZ;
        public RacketIntent(SwingKind kind,uint sequence,ulong epoch,double received,float power,float aimX,float aimZ,bool useGameShotSettings=false){UseGameShotSettings=useGameShotSettings;Kind=kind;Sequence=sequence;Epoch=epoch;ReceivedAt=received;Power=power;AimX=aimX;AimZ=aimZ;}
    }
    public sealed class BleTransportEvent
    {
        public ulong Operation;public string Kind,Id,Name,Status;public byte[] Packet;public double ReceivedAt;
    }
    // Callbacks may arrive on worker threads. They carry the original operation
    // and local monotonic receipt time, never the later drain time or MCU clock.
    public interface IBleTransport:IDisposable
    {
        bool Available {get;} event Action<BleTransportEvent> Received;
        void Scan(ulong operation);void Connect(string id,ulong operation);
        void Write(byte[] packet,ulong operation);void Disconnect();
    }
    public sealed class BleControllerClient:IDisposable
    {
        private readonly IBleTransport transport;
        private readonly Queue<BleTransportEvent> ingress=new Queue<BleTransportEvent>();
        private readonly Queue<RacketIntent> intents=new Queue<RacketIntent>();
        private readonly List<BleDevice> devices=new List<BleDevice>();
        private readonly HashSet<uint> seen=new HashSet<uint>();private readonly Queue<uint> order=new Queue<uint>();
        private ushort lastSequence,commandId;private bool hasSequence,disposed;
        private ulong operation;private double calibrationDeadline,probeDeadline,probeStarted,scanStarted=double.NaN,connectStarted=double.NaN;private ushort probeId;private uint outputSequence;
        public BleLinkState State {get;private set;}
        public string Status {get;private set;}="연결할 기기를 찾아 주세요.";
        public string ConnectedDeviceId {get;private set;}
        public ulong ConnectionEpoch {get;private set;}
        public bool Available=>transport.Available;
        public IReadOnlyList<BleDevice> Devices=>devices.AsReadOnly();
        public bool BondVerified {get;private set;}
        public byte SecurityLevel {get;private set;}
        public bool SecureConnections {get;private set;}
        public ControllerInputProfile InputProfile {get;private set;}=ControllerInputProfile.Motion;
        public bool ButtonInputReady=>InputProfile==ControllerInputProfile.Buttons&&BondVerified&&LinkProbePassed;
        public bool GameplayReady=>State==BleLinkState.Connected&&(ButtonInputReady||InputProfile==ControllerInputProfile.Motion&&Calibrated&&!CalibrationPending);
        public bool CalibrationPending {get;private set;}
        public bool Calibrated {get;private set;}
        public bool LinkProbePending {get;private set;}
        public bool LinkProbePassed {get;private set;}
        public double LinkRoundTripMs {get;private set;}
        public string LinkStatus {get;private set;}="연결 후 통신을 확인할 수 있습니다.";
        public int Rejected {get;private set;}
        public BleControllerClient(IBleTransport transport){this.transport=transport??throw new ArgumentNullException(nameof(transport));State=transport.Available?BleLinkState.Disconnected:BleLinkState.Unavailable;transport.Received+=Receive;}
        private void Receive(BleTransportEvent e){if(e==null)return;lock(ingress){if(disposed)return;if(ingress.Count>=128)ingress.Dequeue();ingress.Enqueue(new BleTransportEvent{Operation=e.Operation,Kind=e.Kind,Id=e.Id,Name=e.Name,Status=e.Status,ReceivedAt=e.ReceivedAt,Packet=e.Packet==null?null:(byte[])e.Packet.Clone()});}}
        private void Reset(){transport.Disconnect();operation++;if(operation==0)operation++;ConnectedDeviceId=null;ConnectionEpoch=0;InputProfile=ControllerInputProfile.Motion;BondVerified=false;SecurityLevel=0;SecureConnections=false;connectStarted=double.NaN;Calibrated=CalibrationPending=LinkProbePending=LinkProbePassed=false;LinkRoundTripMs=0;LinkStatus="연결 후 통신을 확인할 수 있습니다.";ClearInput();hasSequence=false;seen.Clear();order.Clear();lock(ingress)ingress.Clear();}
        public void Scan(){if(disposed||!Available)return;Reset();scanStarted=double.NaN;devices.Clear();State=BleLinkState.Scanning;Status="가까운 라켓을 찾고 있습니다.";transport.Scan(operation);}
        public bool Connect(string id){if(disposed||!Available||!devices.Exists(x=>x.Id==id))return false;Reset();ConnectedDeviceId=id;State=BleLinkState.Connecting;Status="연결을 요청했습니다.";transport.Connect(id,operation);return true;}
        public void Disconnect(){if(disposed)return;Reset();State=Available?BleLinkState.Disconnected:BleLinkState.Unavailable;Status="연결을 해제했습니다.";}
        public bool Calibrate(double now){if(InputProfile==ControllerInputProfile.Buttons||State!=BleLinkState.Connected||CalibrationPending||!Finite(now)||now<0)return false;ClearInput();Calibrated=false;CalibrationPending=true;calibrationDeadline=now+5;if(++commandId==0)commandId=1;transport.Write(PacketCodec.Encode(new ControllerPacket(2,PacketType.Calibrate,0,commandId,0,commandId,0,0,0,0,255)),operation);Status="라켓을 움직이지 말고 보정 완료를 기다려 주세요.";return true;}
        public bool ProbeLink(double now)
        {
            if(State!=BleLinkState.Connected||LinkProbePending||!Finite(now)||now<0)return false;
            if(++probeId==0)probeId=1;
            if(InputProfile==ControllerInputProfile.Buttons)ClearInput();
            LinkProbePending=true;LinkProbePassed=false;probeStarted=now;probeDeadline=now+2;
            LinkStatus="통신 확인 메시지의 실제 응답을 기다립니다.";
            transport.Write(PacketCodec.Encode(new ControllerPacket(2,PacketType.LinkProbe,0,probeId,0,probeId,0,0,0,0,255)),operation);
            return true;
        }
        public void Tick(double now)
        {
            if(disposed||!Finite(now)||now<0)return;
            if(State==BleLinkState.Scanning&&double.IsNaN(scanStarted))scanStarted=now;
            if((State==BleLinkState.Connecting||State==BleLinkState.Pairing)&&double.IsNaN(connectStarted))connectStarted=now;
            for(int count=0;count<128;count++)
            {
                BleTransportEvent e;lock(ingress){if(ingress.Count==0)break;e=ingress.Dequeue();}
                if(e.Operation!=operation){Rejected++;continue;}
                switch(e.Kind)
                {
                    case "device":if(State==BleLinkState.Scanning&&!string.IsNullOrEmpty(e.Id)&&e.Id.Length<=256&&!devices.Exists(x=>x.Id==e.Id)&&devices.Count<64)devices.Add(new BleDevice(e.Id,string.IsNullOrWhiteSpace(e.Name)?"Digital Tennis Racket":CleanName(e.Name)));break;
                    case "pairing":if(State==BleLinkState.Connecting) {State=BleLinkState.Pairing;Status="운영체제의 페어링 승인을 확인해 주세요.";}break;
                    case "security":
                        if((State==BleLinkState.Connecting||State==BleLinkState.Pairing)&&e.Id==ConnectedDeviceId&&LinkSecurity.Decode(e.Packet,out var security)==DecodeError.None&&security.Ready){BondVerified=true;SecurityLevel=security.Level;SecureConnections=security.SecureConnections;}
                        else Rejected++;
                        break;
                    case "connected":if((State==BleLinkState.Connecting||State==BleLinkState.Pairing)&&e.Id==ConnectedDeviceId&&BondVerified){State=BleLinkState.Connected;ConnectionEpoch=operation;hasSequence=false;seen.Clear();order.Clear();Status="페어링·암호화·본딩 확인 완료 · 센서 보정은 별도입니다.";}break;
                    case "packet":ProcessPacket(e,now);break;
                    case "disconnected":Reset();State=BleLinkState.Disconnected;Status="연결이 끊겼습니다. 다시 연결하고 테스트해 주세요.";break;
                    case "unavailable":Reset();State=BleLinkState.Unavailable;Status=e.Status??"Bluetooth를 사용할 수 없습니다.";break;
                    case "error":Reset();State=BleLinkState.Error;Status=e.Status??"연결하지 못했습니다.";break;
                }
            }
            if((State==BleLinkState.Connecting||State==BleLinkState.Pairing)&&now-connectStarted>=60){Reset();State=BleLinkState.Error;Status="페어링 확인 시간이 지났습니다. OS 승인과 기기 전원을 확인해 주세요.";}
            if(LinkProbePending&&now>=probeDeadline){LinkProbePending=false;LinkProbePassed=false;LinkStatus="응답이 없습니다. 기기 연결과 지원 펌웨어를 확인하세요.";}
            if(CalibrationPending&&now>=calibrationDeadline){CalibrationPending=false;Calibrated=false;Status="보정 응답 시간이 지났습니다. 다시 보정해 주세요.";ClearInput();}
            if(State==BleLinkState.Scanning&&now-scanStarted>=4){transport.Disconnect();State=BleLinkState.Disconnected;Status=devices.Count==0?"기기를 찾지 못했습니다. 전원·거리를 확인하고 다시 검색해 주세요.":"검색 완료 · 목록에서 본인 라켓을 선택하세요.";}
        }
        private void ProcessPacket(BleTransportEvent e,double now)
        {
            if(State!=BleLinkState.Connected||!Finite(e.ReceivedAt)||e.ReceivedAt<0||e.ReceivedAt>now+.02||now-e.ReceivedAt>.2||PacketCodec.Decode(e.Packet,out var p)!=DecodeError.None){Rejected++;return;}
            if(hasSequence&&!PacketCodec.Newer(p.Sequence,lastSequence)){Rejected++;return;}lastSequence=p.Sequence;hasSequence=true;
            if(p.Type==PacketType.LinkReply)
            {
                if(!LinkProbePending||p.EventId!=probeId||e.ReceivedAt<probeStarted||e.ReceivedAt>probeDeadline){Rejected++;return;}
                LinkProbePending=false;LinkProbePassed=true;LinkRoundTripMs=(e.ReceivedAt-probeStarted)*1000;
                var profile=(ControllerInputProfile)p.Kind;if(profile!=InputProfile){ClearInput();Calibrated=false;CalibrationPending=false;}InputProfile=profile;
                LinkStatus=ButtonInputReady?"송신·응답 확인 완료 · 버튼 입력 모드 / IMU 사용 안 함":"송신·응답 확인 완료 · 센서 보정과 동작 테스트는 별도입니다.";
                if(ButtonInputReady)Status="본딩 연결 완료 · SW1 서브 / SW2 포핸드 / SW3 백핸드 / SW4 스킬";return;
            }
            if(p.Type==PacketType.Ack){if(CalibrationPending&&p.EventId==commandId&&e.ReceivedAt<=calibrationDeadline){if(p.Kind==1){Status="센서 보정을 진행 중입니다. 라켓을 움직이지 마세요.";return;}CalibrationPending=false;Calibrated=p.Kind==0&&(p.Flags&3)==3;Status=Calibrated?"보정 완료 · 서브·스윙·스킬을 테스트하세요.":"보정하지 못했습니다. 라켓을 정지하고 다시 보정하세요.";}return;}
            if(p.Type==PacketType.Status){if(InputProfile==ControllerInputProfile.Motion&&(p.Flags&3)!=3){Calibrated=false;ClearInput();Status="센서 준비·보정 상태를 확인해 주세요.";}return;}
            // A status flag alone cannot complete this connection's calibration command.
            bool button=p.Type==PacketType.Button;SwingKind kind;
            if(button)
            {
                if(!ButtonInputReady||p.Version!=2||p.Kind<4||p.Kind>7){Rejected++;return;}
                kind=p.Kind==(byte)ButtonKind.Serve?SwingKind.Serve:p.Kind==(byte)ButtonKind.Forehand?SwingKind.Forehand:p.Kind==(byte)ButtonKind.Backhand?SwingKind.Backhand:SwingKind.Skill;
            }
            else
            {
                if(InputProfile!=ControllerInputProfile.Motion||p.Type!=PacketType.Swing||!p.MotionReady||!Calibrated||CalibrationPending){Rejected++;return;}
                kind=(SwingKind)p.Kind;
            }
            uint eventKey=((uint)p.Type<<16)|p.EventId;if(!seen.Add(eventKey)){Rejected++;return;}order.Enqueue(eventKey);while(order.Count>64)seen.Remove(order.Dequeue());
            if(++outputSequence==0)outputSequence=1;if(intents.Count>=32)intents.Dequeue();
            intents.Enqueue(new RacketIntent(kind,outputSequence,ConnectionEpoch,e.ReceivedAt,p.Power/100f,Clamp(p.YawCdeg/4500f),Clamp(p.PitchCdeg/4500f),button));
        }
        public bool TryRead(out RacketIntent value){if(intents.Count==0){value=default;return false;}value=intents.Dequeue();return true;}
        // Drops gameplay events only. It must not cancel pairing/discovery or rewrite time.
        public void ClearInput(){intents.Clear();lock(ingress){var keep=new Queue<BleTransportEvent>();while(ingress.Count>0){var e=ingress.Dequeue();if(e.Kind!="packet"||(PacketCodec.Decode(e.Packet,out var p)==DecodeError.None&&p.Type!=PacketType.Swing&&p.Type!=PacketType.Button))keep.Enqueue(e);}while(keep.Count>0)ingress.Enqueue(keep.Dequeue());}}
        public void Dispose(){if(disposed)return;Reset();lock(ingress)disposed=true;transport.Received-=Receive;transport.Dispose();}
        private static string CleanName(string name)
        {
            var result=new StringBuilder();foreach(char c in name){if(result.Length>=80)break;if(char.GetUnicodeCategory(c)==UnicodeCategory.Format)continue;result.Append(char.IsControl(c)?' ':c=='<'?'‹':c=='>'?'›':c);}return result.ToString();
        }
        private static bool Finite(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);
        private static float Clamp(float x)=>Math.Max(-1,Math.Min(1,x));
    }
}

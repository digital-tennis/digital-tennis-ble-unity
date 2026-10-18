using System;
using DigitalTennis.Controller;
using DigitalTennis.Protocol;
class Fake:IBleTransport
{
 public bool Available{get;set;}=true;public ulong Operation;public byte[] Written;public event Action<BleTransportEvent> Received;
 public void Scan(ulong op){Operation=op;}public void Connect(string id,ulong op){Operation=op;}public void Write(byte[] p,ulong op){Written=p;}public void Disconnect(){}public void Dispose(){}
 public void Event(string kind,double at=1,string id=null,byte[] packet=null,ulong? op=null,string name="Racket")=>Received?.Invoke(new BleTransportEvent{Kind=kind,Id=id,Name=name,Packet=packet,Operation=op??Operation,ReceivedAt=at});
}
static class Program
{
 static int checks;static void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;}
 static byte[] Packet(ushort seq,ushort id,byte kind=1,byte flags=3,PacketType type=PacketType.Swing)=>PacketCodec.Encode(new ControllerPacket(2,type,flags,seq,100,id,kind,type==PacketType.Swing?(byte)80:(byte)0,0,0,255));
 static void Main()
 {
  var radio=new Fake();using var client=new BleControllerClient(radio);Check(!client.Connect("fake"),"unfound identity rejected");client.Scan();radio.Event("device",id:"racket",name:"<size=80>Racket</size>\n");radio.Event("device",id:"racket");client.Tick(1);Check(client.Devices.Count==1,"dedup discovery");Check(!client.Devices[0].Name.Contains("<")&&!client.Devices[0].Name.Contains("\n"),"wireless names cannot inject rich text or control lines");Check(client.Connect("racket"),"found connect");radio.Event("pairing");client.Tick(1);Check(client.State==BleLinkState.Pairing,"pairing distinct");radio.Event("connected",id:"other");client.Tick(1);Check(client.State==BleLinkState.Pairing,"wrong identity");radio.Event("connected",id:"racket");client.Tick(1);Check(client.ConnectionEpoch!=0&&!client.Calibrated,"connection not calibration");
  radio.Event("packet",packet:Packet(1,1));client.Tick(1);Check(!client.TryRead(out _),"motion blocked before calibration");Check(client.Calibrate(1),"start calibration");PacketCodec.Decode(radio.Written,out var command);Check(command.Type==PacketType.Calibrate,"calibration wire");
  radio.Event("packet",1.005,packet:Packet(2,command.EventId,1,3,PacketType.Ack));client.Tick(1.005);Check(client.CalibrationPending&&!client.Calibrated,"BUSY does not finish a pending calibration");
  radio.Event("packet",1.01,packet:Packet(3,command.EventId,0,3,PacketType.Ack));client.ClearInput();client.Tick(1.01);Check(client.Calibrated&&!client.CalibrationPending,"ClearInput preserves ACK");
  ushort seq=3;for(byte kind=1;kind<=8;kind++){radio.Event("packet",1.02,packet:Packet(++seq,(ushort)(100+kind),kind));client.Tick(1.03);Check(client.TryRead(out var input)&&(byte)input.Kind==kind&&input.ReceivedAt==1.02&&input.Power==.8f,"eight action packet to intent");}
  radio.Event("packet",1.04,packet:Packet(++seq,108,8));client.Tick(1.04);Check(!client.TryRead(out _),"repeated event under new packet sequence");
  radio.Event("packet",.5,packet:Packet(++seq,200));client.Tick(1.1);Check(!client.TryRead(out _),"stale receipt");radio.Event("packet",2,packet:Packet(++seq,201));client.Tick(1.1);Check(!client.TryRead(out _),"future receipt");
  byte[] broken=Packet(++seq,202);broken[19]^=1;radio.Event("packet",1.1,packet:broken);client.Tick(1.1);Check(!client.TryRead(out _),"CRC rejected");
  ulong old=radio.Operation;client.Disconnect();client.Scan();radio.Event("device",id:"racket");client.Tick(1.2);client.Connect("racket");radio.Event("connected",id:"racket");client.Tick(1.2);radio.Event("packet",1.2,packet:Packet(++seq,203,8),op:old);client.Tick(1.2);Check(!client.TryRead(out _)&&!client.Calibrated,"old epoch cannot replay held skill");
  client.Calibrate(2);client.Tick(7);Check(!client.CalibrationPending&&!client.Calibrated,"ACK timeout");
  client.Calibrate(8);PacketCodec.Decode(radio.Written,out command);radio.Event("packet",8.1,packet:Packet(100,command.EventId,0,3,PacketType.Ack));client.Tick(8.1);Check(client.Calibrated,"new connection can calibrate");radio.Event("packet",8.2,packet:Packet(101,204));client.ClearInput();client.Tick(8.2);Check(!client.TryRead(out _),"queued motion dropped");
  radio.Event("disconnected");client.Tick(8.3);Check(client.ConnectionEpoch==0&&!client.Calibrated,"disconnect invalidates state");
  client.Scan();radio.Event("device",id:"racket");client.Tick(10);client.Tick(14);Check(client.State==BleLinkState.Disconnected&&client.Devices.Count==1,"bounded discovery exposes connectable results");
  using var missing=new NativeBleProcessTransport("/nonexistent/digital-tennis-ble",()=>9);Check(!missing.Available,"missing native helper honest");
  Console.WriteLine($"ALL {checks} CONTROLLER CHECKS PASSED");
 }
}

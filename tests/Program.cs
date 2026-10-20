using System;
using DigitalTennis.Controller;
using DigitalTennis.Protocol;
class Fake:IBleTransport
{
 public bool Available{get;set;}=true;public ulong Operation;public byte[] Written;public event Action<BleTransportEvent> Received;
 public void Scan(ulong op){Operation=op;}public void Connect(string id,ulong op){Operation=op;}public void Write(byte[] p,ulong op){Written=p;}public void Disconnect(){}public void Dispose(){}
 public void Event(string kind,double at=1,string id=null,byte[] packet=null,ulong? op=null,string name="Racket",bool secured=true){if(kind=="connected"&&secured)Received?.Invoke(new BleTransportEvent{Kind="security",Id=id,Packet=new byte[]{0x44,1,2,3},Operation=op??Operation,ReceivedAt=at});Received?.Invoke(new BleTransportEvent{Kind=kind,Id=id,Name=name,Packet=packet,Operation=op??Operation,ReceivedAt=at});}
}
static class Program
{
 static int checks;static void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;}
 static byte[] Packet(ushort seq,ushort id,byte kind=1,byte flags=3,PacketType type=PacketType.Swing)=>PacketCodec.Encode(new ControllerPacket(2,type,flags,seq,100,id,kind,type==PacketType.Swing?(byte)80:(byte)0,0,0,255));
 static void Main()
 {
  var radio=new Fake();using var client=new BleControllerClient(radio);Check(!client.Connect("fake"),"unfound identity rejected");client.Scan();radio.Event("device",id:"racket",name:"<size=80>Racket</size>\n");radio.Event("device",id:"racket");client.Tick(1);Check(client.Devices.Count==1,"dedup discovery");Check(!client.Devices[0].Name.Contains("<")&&!client.Devices[0].Name.Contains("\n"),"wireless names cannot inject rich text or control lines");Check(client.Connect("racket"),"found connect");radio.Event("pairing");client.Tick(1);Check(client.State==BleLinkState.Pairing,"pairing distinct");radio.Event("connected",id:"other");client.Tick(1);Check(client.State==BleLinkState.Pairing,"wrong identity");radio.Event("connected",id:"racket",secured:false);client.Tick(1);Check(client.State==BleLinkState.Pairing&&!client.BondVerified,"plain GATT connection is not bonding proof");radio.Event("security",id:"racket",packet:new byte[]{0x44,1,1,0});client.Tick(1);Check(!client.BondVerified,"unencrypted security value rejected");radio.Event("security",id:"other",packet:new byte[]{0x44,1,2,3});client.Tick(1);Check(!client.BondVerified,"wrong device bond proof rejected");radio.Event("connected",id:"racket");client.Tick(1);Check(client.BondVerified&&client.SecurityLevel==2&&client.SecureConnections,"current peer encrypted bond proof accepted");Check(client.ConnectionEpoch!=0&&!client.Calibrated,"connection not calibration");
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
  client.Scan();radio.Event("device",id:"racket");client.Tick(20);client.Connect("racket");radio.Event("connected",id:"racket");client.Tick(20);
  Check(client.ProbeLink(20),"sensorless link probe allowed");PacketCodec.Decode(radio.Written,out var probe);Check(probe.Type==PacketType.LinkProbe&&!client.Calibrated,"probe never calibrates");
  radio.Event("packet",20.01,packet:Packet(1,(ushort)(probe.EventId+1),0,0,PacketType.LinkReply));client.Tick(20.01);Check(client.LinkProbePending&&!client.LinkProbePassed,"wrong probe nonce rejected");
  radio.Event("packet",20.02,packet:Packet(2,probe.EventId,0,0,PacketType.LinkReply));client.Tick(20.02);Check(client.LinkProbePassed&&!client.LinkProbePending&&!client.Calibrated&&!client.TryRead(out _)&&client.LinkRoundTripMs>19,"sensorless RX/TX proven independently of motion");
  Check(client.ProbeLink(21),"retest resets previous success");client.Tick(23);Check(!client.LinkProbePending&&!client.LinkProbePassed,"write without response times out");
  radio.Event("packet",23.01,packet:Packet(3,probe.EventId,0,3,PacketType.LinkReply));client.Tick(23.01);Check(!client.LinkProbePassed&&!client.Calibrated,"late response cannot revive test or calibration");
  client.ProbeLink(24);client.Disconnect();Check(!client.LinkProbePending&&!client.LinkProbePassed,"disconnect discards probe result");
  client.Scan();radio.Event("device",id:"racket");client.Tick(1000);client.Connect("racket");radio.Event("pairing");client.Tick(1000);client.Tick(1060);Check(client.State==BleLinkState.Error&&!client.BondVerified&&client.ConnectionEpoch==0,"unapproved OS pairing times out without opening input");
  client.Disconnect();client.Scan();radio.Event("device",id:"racket");client.Tick(1100);client.Connect("racket");radio.Event("connected",1100,id:"racket");client.Tick(1100);
  Check(!client.ButtonInputReady&&!client.GameplayReady,"bond alone does not authorize button gameplay");
  radio.Event("packet",1100.01,packet:Packet(1,300,4,0,PacketType.Button));client.Tick(1100.01);Check(!client.TryRead(out _),"unnegotiated button rejected");
  client.ProbeLink(1101);PacketCodec.Decode(radio.Written,out probe);
  radio.Event("packet",1101.01,packet:Packet(2,(ushort)(probe.EventId+1),1,0,PacketType.LinkReply));client.Tick(1101.01);Check(!client.ButtonInputReady,"wrong nonce cannot select button mode");
  radio.Event("packet",1101.02,packet:Packet(3,probe.EventId,1,0,PacketType.LinkReply));client.Tick(1101.02);
  Check(client.ButtonInputReady&&client.GameplayReady&&!client.Calibrated&&!client.Calibrate(1101.02),"explicit authenticated button profile works without IMU calibration");
  SwingKind[] expected={SwingKind.Serve,SwingKind.Forehand,SwingKind.Backhand,SwingKind.Skill};
  for(byte k=4;k<=7;k++){
   radio.Event("packet",1101.03,packet:Packet((ushort)(k+1),(ushort)(400+k),k,0,PacketType.Button));client.Tick(1101.03);
   Check(client.TryRead(out var input)&&input.Kind==expected[k-4]&&input.UseGameShotSettings&&input.ReceivedAt==1101.03,"physical button semantics to normalized intent");
  }
  radio.Event("packet",1101.04,packet:Packet(9,407,7,0,PacketType.Button));client.Tick(1101.04);Check(!client.TryRead(out _),"three radio retransmissions produce one skill");
  radio.Event("packet",1101.05,packet:Packet(10,500,8,3));client.Tick(1101.05);Check(!client.TryRead(out _),"button mode cannot forge IMU swings");
  radio.Event("packet",1101.06,packet:Packet(11,501,1,0,PacketType.Button));client.Tick(1101.06);Check(!client.TryRead(out _),"legacy confirm is not a serve");
  radio.Event("packet",1101.07,packet:Packet(12,502,4,0,PacketType.Button));client.ClearInput();client.Tick(1101.07);Check(!client.TryRead(out _),"scene/source change clears queued buttons too");
  old=radio.Operation;client.Disconnect();Check(!client.ButtonInputReady&&!client.GameplayReady,"disconnect revokes button mode");
  client.Scan();radio.Event("device",id:"racket");client.Tick(1200);client.Connect("racket");radio.Event("connected",1200,id:"racket");client.Tick(1200);
  radio.Event("packet",1200.01,packet:Packet(13,503,7,0,PacketType.Button),op:old);client.Tick(1200.01);Check(!client.TryRead(out _),"old connection button cannot activate skill");
  using var missing=new NativeBleProcessTransport("/nonexistent/digital-tennis-ble",()=>9);Check(!missing.Available,"missing native helper honest");
  PhysicalChecks.Run();
  Console.WriteLine($"ALL {checks} CONTROLLER CHECKS PASSED");
 }
}

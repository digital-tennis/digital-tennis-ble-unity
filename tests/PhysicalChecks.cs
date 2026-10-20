using System;
using DigitalTennis.Controller;
using DigitalTennis.Protocol;
static class PhysicalChecks
{
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static byte[] Wire(PacketType type,ushort sequence,ushort id,byte kind=0,byte flags=0)=>PacketCodec.Encode(new ControllerPacket(3,type,flags,sequence,100,id,kind,type==PacketType.Motion?(byte)80:(byte)0,0,0,255));
    public static void Run()
    {
        var t=new Fake();using var c=new BleControllerClient(t);c.Scan();t.Event("device",id:"physical");c.Tick(1);c.Connect("physical");t.Event("connected",id:"physical");c.Tick(1);
        t.Event("packet",1.01,packet:Wire(PacketType.Motion,1,10,3,3));c.Tick(1.01);Require(!c.TryRead(out _),"unidentified physical input rejected");
        c.ProbeLink(1.02);PacketCodec.Decode(t.Written,out var probe);
        t.Event("packet",1.03,packet:Wire(PacketType.LinkReply,2,probe.EventId,2));c.Tick(1.03);
        Require(c.PhysicalInputMode&&!c.GameplayReady&&c.InputProfile==ControllerInputProfile.PhysicalMotion,"physical profile negotiated without faking calibration");
        Require(c.Calibrate(1.04),"physical calibration starts");PacketCodec.Decode(t.Written,out var command);
        t.Event("packet",1.05,packet:Wire(PacketType.Ack,3,command.EventId,0,3));c.Tick(1.05);Require(c.GameplayReady,"physical ACK ready");
        t.Event("packet",1.06,packet:Wire(PacketType.Motion,4,11,3,3));c.Tick(1.06);
        Require(c.TryRead(out var motion)&&motion.IsPhysical&&motion.Motion==MotionPattern.OverheadDown&&(byte)motion.Kind==0&&motion.ReceivedAt==1.06,"overhead remains neutral in SDK");
        t.Event("packet",1.07,packet:PacketCodec.Encode(new ControllerPacket(2,PacketType.Swing,3,5,100,12,3,80,0,0,255)));c.Tick(1.07);Require(!c.TryRead(out _),"legacy Serve cannot bypass physical profile");
        t.Event("packet",1.08,packet:Wire(PacketType.Status,6,0));c.Tick(1.08);Require(!c.GameplayReady&&!c.Calibrated,"sensor loss revokes calibration");
        c.ProbeLink(1.09);PacketCodec.Decode(t.Written,out probe);t.Event("packet",1.10,packet:Wire(PacketType.LinkReply,7,probe.EventId,3));c.Tick(1.10);
        Require(c.ButtonInputReady&&c.PhysicalInputMode&&!c.Calibrate(1.11),"physical buttons need no IMU");
        t.Event("packet",1.12,packet:Wire(PacketType.ButtonDown,8,20,4));c.Tick(1.12);
        Require(c.TryRead(out var down)&&down.IsPhysical&&down.ButtonNumber==4&&down.ButtonPressed&&(byte)down.Kind==0,"button number is not Skill in SDK");
        t.Event("packet",1.13,packet:Wire(PacketType.ButtonDown,9,20,4));c.Tick(1.13);Require(!c.TryRead(out _),"wire retry dedup");
        t.Event("packet",1.14,packet:Wire(PacketType.ButtonDown,10,21,4));c.Tick(1.14);Require(!c.TryRead(out _),"held button cannot create another down");
        t.Event("packet",1.15,packet:Wire(PacketType.ButtonUp,11,22,4));c.Tick(1.15);
        Require(c.TryRead(out var up)&&up.IsPhysical&&up.ButtonNumber==4&&!up.ButtonPressed,"release remains observable");
        t.Event("packet",1.16,packet:Wire(PacketType.Motion,12,23,3,3));c.Tick(1.16);Require(!c.TryRead(out _),"button profile cannot impersonate motion");
        t.Event("packet",1.17,packet:Wire(PacketType.ButtonDown,13,24,1));c.ClearInput();c.Tick(1.17);Require(!c.TryRead(out _),"source changes discard physical ingress");
        ulong old=t.Operation;c.Disconnect();c.Scan();t.Event("packet",1.18,packet:Wire(PacketType.ButtonDown,14,25,4),op:old);c.Tick(1.18);Require(!c.TryRead(out _),"old connection cannot replay physical edges");
        Console.WriteLine("PHYSICAL v3 checks passed: neutral motion/edges, profile, calibration, duplicate/held/source/epoch gates");
    }
}

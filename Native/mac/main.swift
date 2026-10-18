import Foundation
import CoreBluetooth

let service = CBUUID(string:"6d7c1000-9d40-4cb7-a381-5e908be0a001")
let tx = CBUUID(string:"6d7c1001-9d40-4cb7-a381-5e908be0a001")
let rx = CBUUID(string:"6d7c1002-9d40-4cb7-a381-5e908be0a001")
func emit(_ fields:[String:String]) {
    guard let bytes=try? JSONSerialization.data(withJSONObject:fields,options:[.sortedKeys]),let line=String(data:bytes,encoding:.utf8) else{return}
    print(line);fflush(stdout)
}
final class RacketCentral:NSObject,CBCentralManagerDelegate,CBPeripheralDelegate {
    var central:CBCentralManager!;var peripheral:CBPeripheral?;var command:CBCharacteristic?;var notify:CBCharacteristic?
    let mode:String;let deviceId:String?
    init(mode:String,id:String?){self.mode=mode;self.deviceId=id;super.init();central=CBCentralManager(delegate:self,queue:.main)}
    func centralManagerDidUpdateState(_ central:CBCentralManager){
        guard central.state == .poweredOn else {
            if central.state != .unknown && central.state != .resetting {emit(["kind":"unavailable","status":"Bluetooth 전원·사용 권한을 확인해 주세요."])}
            return
        }
        if mode == "scan"{central.scanForPeripherals(withServices:[service],options:[CBCentralManagerScanOptionAllowDuplicatesKey:false])}
        else if let id=deviceId,let uuid=UUID(uuidString:id),let found=central.retrievePeripherals(withIdentifiers:[uuid]).first {peripheral=found;found.delegate=self;central.connect(found,options:nil)}
        else {emit(["kind":"error","status":"찾은 기기를 다시 검색해 주세요."])}
    }
    func centralManager(_ central:CBCentralManager,didDiscover peripheral:CBPeripheral,advertisementData:[String:Any],rssi RSSI:NSNumber){emit(["kind":"device","id":peripheral.identifier.uuidString,"name":(advertisementData[CBAdvertisementDataLocalNameKey] as? String) ?? peripheral.name ?? "Digital Tennis Racket"])}
    func centralManager(_ central:CBCentralManager,didConnect peripheral:CBPeripheral){self.peripheral=peripheral;emit(["kind":"pairing"]);peripheral.delegate=self;peripheral.discoverServices([service])}
    func centralManager(_ central:CBCentralManager,didFailToConnect peripheral:CBPeripheral,error:Error?){emit(["kind":"error","status":"라켓에 연결하지 못했습니다."])}
    func centralManager(_ central:CBCentralManager,didDisconnectPeripheral peripheral:CBPeripheral,error:Error?){emit(["kind":"disconnected"])}
    func peripheral(_ peripheral:CBPeripheral,didDiscoverServices error:Error?){guard error == nil,let found=peripheral.services?.first(where:{$0.uuid == service}) else{emit(["kind":"error","status":"라켓 서비스를 찾을 수 없습니다."]);return};peripheral.discoverCharacteristics([tx,rx],for:found)}
    func peripheral(_ peripheral:CBPeripheral,didDiscoverCharacteristicsFor service:CBService,error:Error?){
        guard error == nil,let values=service.characteristics else{emit(["kind":"error","status":"라켓 특성을 확인하지 못했습니다."]);return}
        command=values.first(where:{$0.uuid == rx && $0.properties.contains(.write)})
        notify=values.first(where:{$0.uuid == tx && $0.properties.contains(.notify)})
        guard command != nil,let notify=notify else{emit(["kind":"error","status":"지원하는 라켓 Notify/명령 특성이 없습니다."]);return}
        // The encrypted CCC subscription triggers the OS pairing UI. Never fabricate approval.
        peripheral.setNotifyValue(true,for:notify)
    }
    func peripheral(_ peripheral:CBPeripheral,didUpdateNotificationStateFor characteristic:CBCharacteristic,error:Error?){guard error == nil,characteristic.uuid == tx,characteristic.isNotifying else{emit(["kind":"error","status":"페어링 또는 알림 구독이 완료되지 않았습니다."]);return};emit(["kind":"connected","id":peripheral.identifier.uuidString])}
    func peripheral(_ peripheral:CBPeripheral,didUpdateValueFor characteristic:CBCharacteristic,error:Error?){guard error == nil,characteristic.uuid == tx,let value=characteristic.value,value.count == 20 else{return};emit(["kind":"packet","hex":value.map{String(format:"%02x",$0)}.joined()])}
    func peripheral(_ peripheral:CBPeripheral,didWriteValueFor characteristic:CBCharacteristic,error:Error?){if error != nil{emit(["kind":"error","status":"보정 명령을 전달하지 못했습니다."])}}
    func write(_ hex:String){guard hex.count == 40,let peripheral=peripheral,let command=command else{return};var bytes=[UInt8]();var index=hex.startIndex;while index<hex.endIndex{let next=hex.index(index,offsetBy:2);guard let b=UInt8(hex[index..<next],radix:16) else{return};bytes.append(b);index=next};peripheral.writeValue(Data(bytes),for:command,type:.withResponse)}
}
let args=CommandLine.arguments
guard args.count>=2, args[1] == "scan" || args[1] == "connect" else{emit(["kind":"error","status":"Invalid helper invocation"]);exit(2)}
let app=RacketCentral(mode:args[1],id:args.count>=3 ? args[2]:nil)
DispatchQueue.global().async {while let line=readLine(){DispatchQueue.main.async{app.write(line)}};exit(0)}
RunLoop.main.run()

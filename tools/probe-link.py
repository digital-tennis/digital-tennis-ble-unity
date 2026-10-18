#!/usr/bin/env python3
"""Explicit BLE link check. Never calibrates, selects gameplay input, or flashes a board."""
import argparse,json,queue,secrets,struct,subprocess,threading,time
from pathlib import Path

def crc(data):
    value=0
    for byte in data:
        value^=byte
        for _ in range(8):value=((value<<1)^7 if value&128 else value<<1)&255
    return value

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--helper',type=Path,required=True)
    parser.add_argument('--device',help='Exact identity from a previous --scan; only the tennis service is supported')
    parser.add_argument('--scan',action='store_true')
    parser.add_argument('--out',type=Path,required=True)
    args=parser.parse_args()
    if args.scan==bool(args.device):parser.error('Choose --scan or --device')
    if not args.helper.is_file():parser.error('Build the native helper first')
    command=[str(args.helper.resolve()),'scan'] if args.scan else [str(args.helper.resolve()),'connect',args.device]
    process=subprocess.Popen(command,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,text=True)
    messages=queue.Queue()
    def read():
        for line in process.stdout:
            if len(line)>2048:continue
            try:messages.put(json.loads(line))
            except ValueError:pass
    threading.Thread(target=read,daemon=True).start()
    result={'scope':'Actual native BLE scan/link probe only; no sensor or gameplay proof','connected':False,'reply':False,'bondVerified':False,'devices':[]}
    failed=False;nonce=secrets.randbelow(65535)+1;start=None;deadline=time.monotonic()+(5 if args.scan else 60)
    try:
        while time.monotonic()<deadline:
            try:event=messages.get(timeout=.1)
            except queue.Empty:
                if process.poll() is not None:failed=True;result["status"]="Native helper stopped before completion";break
                continue
            kind=event.get('kind')
            if kind=='device' and args.scan and not any(d['id']==event['id'] for d in result['devices']):result['devices'].append({'id':event['id'],'name':event.get('name')})
            elif kind=='security' and event.get('id')==args.device:
                try:security=bytes.fromhex(event.get('hex',''))
                except ValueError:continue
                if len(security)==4 and security[:2]==bytes([0x44,1]) and 2<=security[2]<=4 and security[3]&~3==0 and security[3]&1:
                    result['bondVerified']=True;result['securityLevel']=security[2];result['secureConnections']=bool(security[3]&2)
            elif kind=='connected' and event.get('id')==args.device and result['bondVerified']:
                result['connected']=True
                body=struct.pack('<BBBBHIHBBhhB',0x44,2,130,0,nonce,0,nonce,0,0,0,0,255)
                start=time.monotonic();deadline=start+2
                process.stdin.write((body+bytes([crc(body)])).hex()+'\n');process.stdin.flush()
            elif kind=='packet' and start is not None:
                try:data=bytes.fromhex(event.get('hex',''))
                except ValueError:continue
                if len(data)==20 and data[:3]==bytes([0x44,2,131]) and crc(data[:19])==data[19] and data[3]&~7==0 and (data[18]<=100 or data[18]==255) and int.from_bytes(data[10:12],'little')==nonce and data[12:18]==bytes(6):
                    result['reply']=True;result['roundTripMs']=round((time.monotonic()-start)*1000,3);result['sensorReady']=bool(data[3]&2);result['calibrated']=bool(data[3]&1);break
            elif kind in ('error','unavailable','disconnected'):
                failed=True;result['status']=event.get('status',kind);break
        result['passed']=not failed and (args.scan or result['connected'] and result['reply'])
    finally:
        try:process.stdin.close()
        except (BrokenPipeError,OSError):pass
        try:process.wait(timeout=2)
        except subprocess.TimeoutExpired:process.kill();process.wait()
        args.out.parent.mkdir(parents=True,exist_ok=True);args.out.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')
    print(json.dumps(result,ensure_ascii=False));return 0 if result['passed'] else 1
if __name__=='__main__':raise SystemExit(main())

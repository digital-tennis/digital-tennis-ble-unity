#!/usr/bin/env python3
"""Build native helpers without starting Bluetooth or changing OS permissions."""
import argparse,pathlib,subprocess,shutil
p=argparse.ArgumentParser();p.add_argument('--platform',choices=['mac','windows'],required=True);p.add_argument('--out',type=pathlib.Path,required=True);a=p.parse_args();root=pathlib.Path(__file__).resolve().parents[1];out=a.out.resolve();out.mkdir(parents=True,exist_ok=True)
if a.platform=='mac':
    products=[]
    for arch in ('arm64','x86_64'):
        binary=out/('ble-'+arch)
        subprocess.run(['swiftc',str(root/'Native/mac/main.swift'),'-target',arch+'-apple-macosx12.0','-framework','CoreBluetooth','-Xlinker','-sectcreate','-Xlinker','__TEXT','-Xlinker','__info_plist','-Xlinker',str(root/'Native/mac/Info.plist'),'-o',str(binary)],check=True);products.append(binary)
    subprocess.run(['lipo','-create',*map(str,products),'-output',str(out/'digital-tennis-ble')],check=True)
    for product in products:product.unlink()
    subprocess.run(['codesign','--force','--sign','-',str(out/'digital-tennis-ble')],check=True)
else:
    subprocess.run(['dotnet','publish',str(root/'Native/windows/BleHelper.csproj'),'-c','Release','-o',str(out),'-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','--nologo','-v','quiet'],check=True)
    for symbols in out.glob('*.pdb'):symbols.unlink()
print(out)

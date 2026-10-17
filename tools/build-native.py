#!/usr/bin/env python3
"""Build native helpers without starting Bluetooth or changing OS permissions."""
import argparse,pathlib,subprocess,shutil,json,hashlib
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
    # Self-contained delivery includes the Microsoft runtime. Preserve the
    # notices supplied by every restored package, not only our helper source.
    assets=json.loads((root/'Native/windows/obj/project.assets.json').read_text())
    inventory=[]
    packages={name:library['path'] for name,library in assets['libraries'].items() if library.get('type')=='package'}
    # Runtime/WindowsDesktop packs are downloads, not normal library entries.
    # They are redistributed by self-contained publish and need their notices too.
    for framework in assets['project']['frameworks'].values():
        for dependency in framework.get('downloadDependencies',[]):
            version=dependency['version'].strip('[]').split(',')[0].strip()
            name=dependency['name']+'/'+version
            packages[name]=dependency['name'].lower()+'/'+version
    for name,relative in packages.items():
        package=next((pathlib.Path(folder)/relative for folder in assets['packageFolders'] if (pathlib.Path(folder)/relative).is_dir()),None)
        if package is None:continue
        notices=[path for path in package.iterdir() if path.is_file() and ('license' in path.name.lower() or 'notice' in path.name.lower())]
        destination=out/'Licenses'/name.replace('/','-');destination.mkdir(parents=True,exist_ok=True)
        for notice in notices:shutil.copyfile(notice,destination/notice.name)
        inventory.append({'package':name,'notices':[path.name for path in notices]})
    for prefix in ('Microsoft.NETCore.App.Runtime.','Microsoft.WindowsDesktop.App.Runtime.'):
        assert any(item['package'].startswith(prefix) and any('license' in name.lower() for name in item['notices']) for item in inventory), 'Missing redistributed runtime license: '+prefix
    (out/'Licenses/packages.json').write_text(json.dumps(inventory,indent=2)+'\n')
sources={str(path.relative_to(root)):hashlib.sha256(path.read_bytes()).hexdigest() for path in (root/'Native').rglob('*') if path.is_file() and not any(part in ('bin','obj') for part in path.relative_to(root).parts) and path.suffix in ('.cs','.csproj','.swift','.plist')}
(out/'native-build.json').write_text(json.dumps({'platform':a.platform,'sourceHashes':sources,'sdk':subprocess.check_output(['dotnet','--version'],text=True).strip() if a.platform=='windows' else subprocess.check_output(['swiftc','--version'],text=True).strip()},indent=2)+'\n')
print(out)

"""
Generates one .csproj per Unity assembly definition (asmdef) in game/Assets, plus the Input System
package, so the Unity-side C# can be compile-checked with `dotnet build` against the real Unity
managed assemblies — without a Unity license or editor install.

Usage: gen.py <unity-data-dir> <inputsystem-dir> <out-dir> <mode>
  mode: player (iOS player), server (Linux dedicated server), editor, ios-editor
"""
import glob
import json
import os
import sys

data_dir, inputsystem_dir, out_root, mode = sys.argv[1:5]
repo = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
game_assets = os.path.join(repo, 'game', 'Assets')
managed = os.path.join(data_dir, 'Managed')
out = os.path.join(out_root, mode)
os.makedirs(out, exist_ok=True)

editor = mode in ('editor', 'ios-editor')
defines = []
for year in range(2017, 2024):
    for minor in range(1, 5):
        defines.append(f'UNITY_{year}_{minor}_OR_NEWER')
for minor in range(0, 4):
    defines.append(f'UNITY_6000_{minor}_OR_NEWER')
defines += ['UNITY_5_3_OR_NEWER', 'UNITY_5_4_OR_NEWER', 'UNITY_5_5_OR_NEWER', 'UNITY_5_6_OR_NEWER', 'UNITY_6000_3', 'UNITY_6000',
            'ENABLE_INPUT_SYSTEM', 'ENABLE_LEGACY_INPUT_MANAGER', 'NET_STANDARD_2_1', 'NET_STANDARD', 'CSHARP_7_3_OR_NEWER',
            # Input System versionDefines that apply to this project (physics module present, Unity 6000.3).
            'UNITY_INPUT_SYSTEM_ENABLE_PHYSICS', 'UNITY_INPUT_SYSTEM_PLATFORM_SCROLL_DELTA', 'UNITY_INPUT_SYSTEM_INPUT_MODULE_SCROLL_DELTA',
            'UNITY_INPUT_SYSTEM_SENDPOINTERHOVERTOPARENT', 'UNITY_INPUT_SYSTEM_PLATFORM_POLLING_FREQUENCY',
            'UNITY_INPUT_SYSTEM_INPUT_MODULE_NAVIGATION_DEVICE_TYPE']
if editor:
    defines += ['UNITY_EDITOR', 'UNITY_EDITOR_64', 'UNITY_EDITOR_LINUX']
if mode in ('player', 'ios-editor'):
    defines += ['UNITY_IOS', 'UNITY_IPHONE']
if mode == 'server':
    defines += ['UNITY_SERVER', 'UNITY_STANDALONE', 'UNITY_STANDALONE_LINUX']
if not editor:
    defines += ['ENABLE_IL2CPP']

engine_refs = sorted(glob.glob(os.path.join(managed, 'UnityEngine', 'UnityEngine.*.dll')))
# Module assemblies only: Managed/UnityEditor.dll contains the same types again.
editor_refs = sorted(glob.glob(os.path.join(managed, 'UnityEngine', 'UnityEditor.*.dll')))
if mode == 'ios-editor':
    editor_refs += sorted(glob.glob(os.path.join(data_dir, 'PlaybackEngines', 'iOSSupport', 'UnityEditor.iOS.Extensions*.dll')))

projects = {}


def add_asmdef(path, exclude=()):
    d = json.load(open(path))
    name = d['name']
    for constraint in d.get('defineConstraints', []):
        negated = constraint.startswith('!')
        present = constraint.lstrip('!') in defines
        if negated == present:
            print(f'  skip {name}: define constraint {constraint}')
            return
    if d.get('includePlatforms', []) == ['Editor'] and not editor:
        print(f'  skip {name}: editor only')
        return
    projects[name] = dict(src=os.path.dirname(path), refs=d.get('references', []), unsafe=d.get('allowUnsafeCode', False),
                          engine=not d.get('noEngineReferences', False), exclude=list(exclude))


add_asmdef(os.path.join(inputsystem_dir, 'Unity.InputSystem.asmdef'),
           exclude=[os.path.join(inputsystem_dir, 'Runtime', 'Plugins', 'InputForUI'), os.path.join(inputsystem_dir, 'Editor', 'Plugins', 'InputForUI')])
for path in sorted(glob.glob(os.path.join(game_assets, '**', '*.asmdef'), recursive=True)):
    if os.sep + 'Tests' + os.sep in path:
        continue  # tests compile in sim/ (dotnet test)
    if os.sep + 'Rendering' + os.sep in path:
        print('  skip Kindling.Editor.Rendering: needs the URP package')
        continue
    add_asmdef(path)

for name, p in projects.items():
    refs = []
    for r in p['refs']:
        if r in projects:
            refs.append(f'    <ProjectReference Include="../{r}/{r}.csproj" />')
        elif r != 'Unity.ugui':  # Input System's optional UI integration (disabled by define)
            print(f'  {name}: unresolved reference {r}')
    nested = [os.path.dirname(q) for q in glob.glob(os.path.join(p['src'], '**', '*.asmdef'), recursive=True) if os.path.dirname(q) != p['src']]
    removes = ''.join(f'<Compile Remove="{e}/**/*.cs" />' for e in p['exclude'] + nested)
    dlls = (engine_refs + (editor_refs if editor else [])) if p['engine'] else []
    dll_items = '\n'.join(f'    <Reference Include="{os.path.splitext(os.path.basename(x))[0]}"><HintPath>{x}</HintPath><Private>false</Private></Reference>' for x in dlls)
    os.makedirs(os.path.join(out, name), exist_ok=True)
    with open(os.path.join(out, name, f'{name}.csproj'), 'w') as f:
        f.write(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <AssemblyName>{name}</AssemblyName>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <AllowUnsafeBlocks>{str(p['unsafe']).lower()}</AllowUnsafeBlocks>
    <DefineConstants>{';'.join(defines)}</DefineConstants>
    <NoWarn>CS1591;CS0618;CS0649;CS0414;CS0169;CS0067;CS8632;CS1701;CS1702</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="{p['src']}/**/*.cs" />
    {removes}
  </ItemGroup>
  <ItemGroup>
{dll_items}
{chr(10).join(refs)}
  </ItemGroup>
</Project>
''')
print(f'{mode}: {", ".join(projects)}')

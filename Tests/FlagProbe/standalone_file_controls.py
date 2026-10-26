"""Prepare bounded loader controls, not distributable standalone flags.

The pointer-folder control is intentionally session-specific. The native
reference loader accepts folder index -1 followed by an eight-byte folder
pointer. This diagnostic never writes process memory and must be quarantined
before restarting. It is NOT a persistence solution.
"""
import json
from pathlib import Path
import shutil
import struct
import subprocess

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'artifacts/flag-standalone-20261021/file-controls'
LIVE = Path.home() / 'Documents/Trackmania/Items/TMIS_FileControls'
EXTRACT = Path.home() / 'OpenplanetNext/Extract/GameData'
SOURCE = ROOT / 'artifacts/flag-standalone-20261021/shader-paths/TMIS_Path_Relative0.Item.Gbx'


def prepare(folder_pointer, process_id, stage_session=False):
    active = subprocess.check_output(['powershell', '-NoProfile', '-Command',
        '(Get-Process Trackmania -ErrorAction Stop).Id'], text=True).split()
    if active != [str(process_id)]:
        raise RuntimeError('Refuse a reference for a different game process')
    output = OUT / f'session-{process_id}'
    output.mkdir(parents=True, exist_ok=False)
    LIVE.mkdir(parents=True, exist_ok=True)
    data = SOURCE.read_bytes()
    header_size = struct.unpack_from('<I', data, 13)[0]
    pos = 17 + header_size + 4  # after node count
    start = pos

    def integer():
        nonlocal pos
        value = struct.unpack_from('<i', data, pos)[0]
        pos += 4
        return value

    def string():
        nonlocal pos
        length = integer()
        value = data[pos:pos+length]
        pos += length
        return value

    count = integer()
    assert count == 1, 'This control requires the single known shader reference'
    ancestor = integer()

    def directories():
        for _ in range(integer()):
            string()
            directories()

    directories()
    flags, filename = integer(), string()
    node_index, use_file, directory = integer(), integer(), integer()
    assert use_file == 1 and filename == b'Tech3_Warp_TDiffSpec_VertexTween.Shader.Gbx'
    prefix = struct.pack('<iiii', 1, 0, 0, flags)
    reference = struct.pack('<i', len(filename)) + filename
    reference += struct.pack('<iiiQ', node_index, use_file, -1, folder_pointer)
    pointer_file = output / 'TMIS_Control_SessionFolderPointer.Item.Gbx'
    pointer_file.write_bytes(data[:start] + prefix + reference + data[pos:])
    if stage_session:
        shutil.copy2(pointer_file, LIVE / pointer_file.name)

    local_file = output / 'TMIS_Control_LocalShader.Item.Gbx'
    shutil.copy2(SOURCE, local_file)
    shutil.copy2(local_file, LIVE / local_file.name)
    shader_name = filename.decode()
    target = LIVE / 'GameData/Techno3/Media/Shader'
    target.mkdir(parents=True, exist_ok=True)
    # Preserve actual extracted bytes and complete shader passes. Do not create
    # fake HLSL or substitute unrelated resources for missing dependencies.
    for name in [shader_name, shader_name.replace('.Shader.', '_DefRead.Shader.'),
                 shader_name.replace('.Shader.', '_DefWrite.Shader.')]:
        source = EXTRACT / 'Techno3/Media/Shader' / name
        if source.exists():
            shutil.copy2(source, target / name)
    (output / 'manifest.json').write_text(json.dumps({
        'source': str(SOURCE), 'folderPointer': hex(folder_pointer),
        'processScope': f'PID {process_id} only; quarantine pointer file before restart',
        'controls': [str(pointer_file), str(local_file)],
        'standaloneSuccess': False,
        'sessionPointerStaged': stage_session,
    }, indent=2))
    print(json.dumps({'live': str(LIVE), 'pointer': hex(folder_pointer)}))


if __name__ == '__main__':
    import sys
    prepare(int(sys.argv[1], 0), int(sys.argv[2]), '--stage-session' in sys.argv[3:])

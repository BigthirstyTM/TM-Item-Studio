"""Build the temporary read-only GPU diagnostic with isolated portable tools."""
from pathlib import Path
import subprocess
root=Path(__file__).resolve().parents[2]
r=root/'artifacts/flag-vanilla-steps-20261021/gpu-tools/native-probe'
zig=r/'zig-windows-x86_64-0.13.0/zig.exe'
log=[]
def run(args):
    result=subprocess.run([str(zig)]+[str(x) for x in args],capture_output=True,text=True,cwd=root)
    log.append(result.stdout+result.stderr)
    (r/'build.txt').write_text('\n'.join(log))
    print(result.stdout+result.stderr,flush=True)
    if result.returncode:raise SystemExit(result.returncode)
objects=[]
for source in ['buffer.c','hook.c','trampoline.c','hde/hde64.c']:
    out=r/(Path(source).stem+'.o')
    run(['cc','-O2','-c',r/'minhook/src'/source,'-o',out])
    objects.append(out)
run(['cc','-fms-extensions','-fno-exceptions','-fno-rtti','-O2','-shared',root/'Tests/FlagProbe/GpuFlagReadback.cpp',*objects,
     '-I'+str(r/'minhook/include'),'-ld3d11','-ldxguid','-luser32','-lkernel32','-o',r/'GpuFlagReadback.dll'])
run(['cc','-O2',root/'Tests/FlagProbe/GpuFlagInject.c','-o',r/'GpuFlagInject.exe'])
print('Built fixed-purpose GPU probe and loader')

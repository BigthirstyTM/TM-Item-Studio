// Fixed-path loader for this one local diagnostic, never a generic plugin route.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <wchar.h>
int main(int argc,char** argv) {
    if(argc!=2)return 2;
    DWORD pid=(DWORD)strtoul(argv[1],0,10);
    HANDLE p=OpenProcess(PROCESS_CREATE_THREAD|PROCESS_QUERY_LIMITED_INFORMATION|PROCESS_VM_OPERATION|PROCESS_VM_WRITE|PROCESS_VM_READ,FALSE,pid);
    if(!p)return 3;
    wchar_t image[1024];DWORD n=1024;
    if(!QueryFullProcessImageNameW(p,0,image,&n) || _wcsicmp(wcsrchr(image,L'\\')+1,L"Trackmania.exe")){CloseHandle(p);return 4;}
    const wchar_t dll[]=L"C:/Users/PC/.copilot/repos/TM-Item-Studio/artifacts/flag-vanilla-steps-20261021/gpu-tools/native-probe/GpuFlagReadback.dll";
    void* remote=VirtualAllocEx(p,0,sizeof(dll),MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);
    if(!remote || !WriteProcessMemory(p,remote,dll,sizeof(dll),0)){CloseHandle(p);return 5;}
    HANDLE t=CreateRemoteThread(p,0,0,(LPTHREAD_START_ROUTINE)GetProcAddress(GetModuleHandleW(L"kernel32.dll"),"LoadLibraryW"),remote,0,0);
    if(!t){VirtualFreeEx(p,remote,0,MEM_RELEASE);CloseHandle(p);return 6;}
    DWORD wait=WaitForSingleObject(t,3000),code=0;
    if(wait==WAIT_OBJECT_0){GetExitCodeThread(t,&code);VirtualFreeEx(p,remote,0,MEM_RELEASE);}
    printf("pid=%lu loaderWait=%lu moduleLow32=%lx\n",pid,wait,code);
    CloseHandle(t);CloseHandle(p);return wait==WAIT_OBJECT_0 && code?0:7;
}

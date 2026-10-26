// Local, bounded D3D11 diagnostic. Optional fixed-fixture binding experiment.
// MinHook v1.3.4 is separately retained with its BSD-style license in artifacts.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11_1.h>
#include <stdio.h>
#include <string.h>
#include <stdint.h>
#include <fcntl.h>
#include <io.h>
#include <sys/stat.h>
#include "MinHook.h"

static const char* Root="C:/Users/PC/.copilot/repos/TM-Item-Studio/artifacts/flag-vanilla-steps-20261021/gpu-tools/native-probe/";
static HMODULE Module;
static CRITICAL_SECTION Lock;
static volatile LONG Active=0, Stopping=0, Captures=0;
static __declspec(thread) bool Inside=false;
static FILE* Log=nullptr;
static uint64_t Seen[128]{};
static UINT SeenCount=0;
static bool RepairEnabled=false;
struct RepairResource { ID3D11Buffer* source; ID3D11ShaderResourceView* view; };
static RepairResource Repairs[8]{};
static UINT RepairCount=0;
struct CallbackGuard { CallbackGuard(){InterlockedIncrement(&Active);} ~CallbackGuard(){InterlockedDecrement(&Active);} };
template<class T> static void Release(T*& p) { if(p) p->Release(); p=nullptr; }

static bool Read(ID3D11DeviceContext* ctx,ID3D11Buffer* buf,void* out,UINT bytes,UINT offset=0) {
    D3D11_BUFFER_DESC d{};buf->GetDesc(&d);
    if(offset>d.ByteWidth || bytes>d.ByteWidth-offset || bytes>4*1024*1024) return false;
    ID3D11Device* dev=nullptr;ctx->GetDevice(&dev);
    d.ByteWidth=bytes;d.BindFlags=0;d.MiscFlags=0;d.StructureByteStride=0;d.Usage=D3D11_USAGE_STAGING;d.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    ID3D11Buffer* stage=nullptr;
    HRESULT hr=dev->CreateBuffer(&d,nullptr,&stage);Release(dev);
    if(FAILED(hr))return false;
    D3D11_BOX box{offset,0,0,offset+bytes,1,1};ctx->CopySubresourceRegion(stage,0,0,0,0,buf,0,&box);
    D3D11_MAPPED_SUBRESOURCE mapped{};hr=ctx->Map(stage,0,D3D11_MAP_READ,0,&mapped);
    if(SUCCEEDED(hr)){memcpy(out,mapped.pData,bytes);ctx->Unmap(stage,0);}
    Release(stage);return SUCCEEDED(hr);
}
static void SaveBuffer(ID3D11DeviceContext* ctx,ID3D11Buffer* buf,int n,int slot) {
    D3D11_BUFFER_DESC d{};buf->GetDesc(&d);
    if(!d.ByteWidth)return;
    UINT cap=slot>=119?512u:4u*1024*1024;
    UINT bytes=d.ByteWidth<cap?d.ByteWidth:cap;
    if(d.StructureByteStride)bytes-=bytes%d.StructureByteStride;
    void* data=HeapAlloc(GetProcessHeap(),0,bytes);
    if(data && Read(ctx,buf,data,bytes)) {
        char path[512];snprintf(path,sizeof(path),"%sdraw-%02d-t%d.bin",Root,n,slot);
        FILE* f=fopen(path,"wb");if(f){fwrite(data,1,bytes,f);fclose(f);}
    }
    if(data)HeapFree(GetProcessHeap(),0,data);
}
// Diagnostic only. Restore the previous SRV after every original draw. The
// existing assets and buffers are never changed. Restricted to known fixtures.
struct RepairGuard {
    ID3D11DeviceContext* ctx; ID3D11ShaderResourceView* previous=nullptr;
    bool changed=false;
    RepairGuard(ID3D11DeviceContext* x,UINT indices):ctx(x) {
        if(!RepairEnabled || Stopping || indices!=726)return;
        ID3D11Buffer* cb=nullptr;ctx->VSGetConstantBuffers(0,1,&cb);
        uint32_t c[20]{};bool ok=cb && Read(ctx,cb,c,sizeof(c));Release(cb);
        if(!ok || c[1]!=144 || c[2]!=86 || c[0]>=16)return;
        ID3D11ShaderResourceView* inst=nullptr;ctx->VSGetShaderResources(119,1,&inst);
        ID3D11Resource* ir=nullptr;if(inst)inst->GetResource(&ir);Release(inst);
        D3D11_RESOURCE_DIMENSION kind=D3D11_RESOURCE_DIMENSION_UNKNOWN;if(ir)ir->GetType(&kind);
        float t[8]{};
        ok=kind==D3D11_RESOURCE_DIMENSION_BUFFER && Read(ctx,static_cast<ID3D11Buffer*>(ir),t,sizeof(t),c[0]*32);Release(ir);
        if(!ok || t[4]<1359 || t[4]>1481 || t[5]<19 || t[5]>20 || t[6]<1359.9f || t[6]>1360.1f)return;
        ID3D11Buffer* vb=nullptr;UINT stride=0,offset=0;ctx->IAGetVertexBuffers(0,1,&vb,&stride,&offset);
        D3D11_BUFFER_DESC desc{};if(vb)vb->GetDesc(&desc);
        if(!vb || stride!=28 || offset!=0 || desc.ByteWidth!=144*86*28) {Release(vb);return;}
        ID3D11ShaderResourceView* replacement=nullptr;
        for(UINT i=0;i<RepairCount;i++)if(Repairs[i].source==vb)replacement=Repairs[i].view;
        if(!replacement && RepairCount<8) {
            void* data=HeapAlloc(GetProcessHeap(),0,desc.ByteWidth);
            if(data && Read(ctx,vb,data,desc.ByteWidth)) {
                ID3D11Device* dev=nullptr;ctx->GetDevice(&dev);
                D3D11_BUFFER_DESC nd{};nd.ByteWidth=desc.ByteWidth;nd.Usage=D3D11_USAGE_IMMUTABLE;
                nd.BindFlags=D3D11_BIND_SHADER_RESOURCE;nd.MiscFlags=D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;nd.StructureByteStride=28;
                D3D11_SUBRESOURCE_DATA initial{data,0,0};ID3D11Buffer* copy=nullptr;
                HRESULT hr=dev->CreateBuffer(&nd,&initial,&copy);
                if(SUCCEEDED(hr))hr=dev->CreateShaderResourceView(copy,nullptr,&replacement);
                Release(copy);Release(dev);
                if(SUCCEEDED(hr)) {vb->AddRef();Repairs[RepairCount++]={vb,replacement};
                    fprintf(Log,"{\"event\":\"diagnostic-binding-copy\",\"worldX\":%.6f,\"source\":\"%p\",\"bytes\":%u}\n",t[4],vb,desc.ByteWidth);fflush(Log);}
            }
            if(data)HeapFree(GetProcessHeap(),0,data);
        }
        Release(vb);
        if(replacement){ctx->VSGetShaderResources(0,1,&previous);ctx->VSSetShaderResources(0,1,&replacement);changed=true;}
    }
    ~RepairGuard(){if(changed){ctx->VSSetShaderResources(0,1,&previous);Release(previous);}}
};
static void Probe(ID3D11DeviceContext* ctx,const char* call,UINT a,UINT b,UINT c,INT d,UINT e) {
    if(Inside || Stopping || Captures>=128)return;
    if(a!=726 && a!=216 && a!=54 && a!=24)return;
    Inside=true;EnterCriticalSection(&Lock);
    if(Stopping || Captures>=128 || ctx->GetType()!=D3D11_DEVICE_CONTEXT_IMMEDIATE)goto done;
    {
        ID3D11ShaderResourceView* srv=nullptr;ctx->VSGetShaderResources(0,1,&srv);
        ID3D11Resource* res=nullptr;if(srv)srv->GetResource(&res);
        D3D11_RESOURCE_DIMENSION kind=D3D11_RESOURCE_DIMENSION_UNKNOWN;if(res)res->GetType(&kind);
        auto* buf=kind==D3D11_RESOURCE_DIMENSION_BUFFER?static_cast<ID3D11Buffer*>(res):nullptr;
        D3D11_BUFFER_DESC desc{};if(buf)buf->GetDesc(&desc);
        ID3D11Buffer* cb=nullptr;UINT first=0,count=0;
        ID3D11DeviceContext1* ctx1=nullptr;
        if(SUCCEEDED(ctx->QueryInterface(__uuidof(ID3D11DeviceContext1),(void**)&ctx1))) {
            ctx1->VSGetConstantBuffers1(0,1,&cb,&first,&count);Release(ctx1);
        } else ctx->VSGetConstantBuffers(0,1,&cb);
        uint32_t constants[20]{};
        bool valid=cb && Read(ctx,cb,constants,sizeof(constants),first*16);
        Release(cb);
        ID3D11VertexShader* shader=nullptr;ctx->VSGetShader(&shader,nullptr,nullptr);
        ID3D11Buffer* vb=nullptr;UINT vstride=0,voffset=0;ctx->IAGetVertexBuffers(0,1,&vb,&vstride,&voffset);
        uint64_t key=(uint64_t)shader^((uint64_t)buf<<1)^((uint64_t)vb<<3)^((uint64_t)constants[0]<<9)^((uint64_t)constants[1]<<17)^((uint64_t)a<<33)^((uint64_t)b<<40)^((uint64_t)c<<45)^((uint64_t)(UINT)d<<5);
        bool seen=false;for(UINT k=0;k<SeenCount;k++)if(Seen[k]==key)seen=true;
        if(seen){Release(shader);Release(vb);Release(res);Release(srv);goto done;}
        Seen[SeenCount++]=key;
        int n=(int)InterlockedIncrement(&Captures);
        if(a==726) {
            void* stack[16]{};USHORT depth=CaptureStackBackTrace(0,16,stack,nullptr);
            fprintf(Log,"{\"event\":\"draw-stack\",\"draw\":%d,\"addresses\":[",n);
            for(USHORT i=0;i<depth;i++)fprintf(Log,"%s\"%p\"",i?",":"",stack[i]);
            fprintf(Log,"]}\n");
        }
        D3D11_SHADER_RESOURCE_VIEW_DESC sd{};if(srv)srv->GetDesc(&sd);
        fprintf(Log,"{\"event\":\"candidate\",\"draw\":%d,\"t0Kind\":%d,\"vb\":\"%p\",\"vbStride\":%u,\"vbOffset\":%u,\"cbValid\":%s}\n",n,(int)kind,vb,vstride,voffset,valid?"true":"false");
        fprintf(Log,"{\"draw\":%d,\"call\":\"%s\",\"args\":[%u,%u,%u,%d,%u],\"buffer\":\"%p\",\"srv\":\"%p\",\"shader\":\"%p\",\"bytes\":%u,\"stride\":%u,\"firstElement\":%u,\"elements\":%u,\"cbFirst\":%u,\"cb\":[",n,call,a,b,c,d,e,buf,srv,shader,desc.ByteWidth,desc.StructureByteStride,sd.Buffer.FirstElement,sd.Buffer.NumElements,first);
        for(int i=0;i<20;i++)fprintf(Log,"%s%u",i?",":"",constants[i]);
        fprintf(Log,"]}\n");fflush(Log);Release(shader);
        if(buf)SaveBuffer(ctx,buf,n,0);
        if(vb)SaveBuffer(ctx,vb,n,1);Release(vb);
        for(UINT slot=119;slot<=120;slot++) {
            ID3D11ShaderResourceView* aux=nullptr;ctx->VSGetShaderResources(slot,1,&aux);
            if(aux) {
                ID3D11Resource* ar=nullptr;aux->GetResource(&ar);D3D11_RESOURCE_DIMENSION ak;ar->GetType(&ak);
                fprintf(Log,"{\"event\":\"aux\",\"draw\":%d,\"slot\":%u,\"kind\":%d}\n",n,slot,(int)ak);
                if(ak==D3D11_RESOURCE_DIMENSION_BUFFER)SaveBuffer(ctx,static_cast<ID3D11Buffer*>(ar),n,slot);
                Release(ar);Release(aux);
            }
        }
        Release(res);Release(srv);
    }
done:
    LeaveCriticalSection(&Lock);Inside=false;
}
typedef void(STDMETHODCALLTYPE* DrawIndexedFn)(ID3D11DeviceContext*,UINT,UINT,INT);
typedef void(STDMETHODCALLTYPE* DrawFn)(ID3D11DeviceContext*,UINT,UINT);
typedef void(STDMETHODCALLTYPE* DrawIndexedInstancedFn)(ID3D11DeviceContext*,UINT,UINT,UINT,INT,UINT);
typedef void(STDMETHODCALLTYPE* DrawInstancedFn)(ID3D11DeviceContext*,UINT,UINT,UINT,UINT);
typedef void(STDMETHODCALLTYPE* IndirectFn)(ID3D11DeviceContext*,ID3D11Buffer*,UINT);
static DrawIndexedFn OrigDI;static DrawFn OrigD;static DrawIndexedInstancedFn OrigDII;static DrawInstancedFn OrigDI2;
static IndirectFn OrigDIII,OrigDII2;
static void STDMETHODCALLTYPE HookDI(ID3D11DeviceContext* x,UINT a,UINT b,INT c){CallbackGuard guard;Probe(x,"DrawIndexed",a,b,0,c,0);RepairGuard repair(x,a);OrigDI(x,a,b,c);}
static void STDMETHODCALLTYPE HookD(ID3D11DeviceContext* x,UINT a,UINT b){CallbackGuard guard;Probe(x,"Draw",a,b,0,0,0);OrigD(x,a,b);}
static void STDMETHODCALLTYPE HookDII(ID3D11DeviceContext* x,UINT a,UINT b,UINT c,INT d,UINT e){CallbackGuard guard;Probe(x,"DrawIndexedInstanced",a,b,c,d,e);RepairGuard repair(x,a);OrigDII(x,a,b,c,d,e);}
static void STDMETHODCALLTYPE HookDI2(ID3D11DeviceContext* x,UINT a,UINT b,UINT c,UINT d){CallbackGuard guard;Probe(x,"DrawInstanced",a,b,c,(INT)d,0);OrigDI2(x,a,b,c,d);}
static void STDMETHODCALLTYPE HookDIII(ID3D11DeviceContext* x,ID3D11Buffer* a,UINT b){CallbackGuard guard;Probe(x,"DrawIndexedInstancedIndirect",b,0,0,0,0);OrigDIII(x,a,b);}
static void STDMETHODCALLTYPE HookDII2(ID3D11DeviceContext* x,ID3D11Buffer* a,UINT b){CallbackGuard guard;Probe(x,"DrawInstancedIndirect",b,0,0,0,0);OrigDII2(x,a,b);}
static DWORD WINAPI Worker(void*) {
    char gate[512];snprintf(gate,sizeof(gate),"%sgpu-readback.enable",Root);
    if(GetFileAttributesA(gate)==INVALID_FILE_ATTRIBUTES) {FreeLibraryAndExitThread(Module,0);return 0;}
    char path[512];snprintf(path,sizeof(path),"%sreadback.jsonl",Root);
    int fd=_open(path,_O_WRONLY|_O_CREAT|_O_EXCL|_O_TEXT,_S_IREAD|_S_IWRITE);Log=fd<0?nullptr:_fdopen(fd,"w");
    if(!Log){FreeLibraryAndExitThread(Module,1);return 1;}
    InitializeCriticalSection(&Lock);
    char repairGate[512];snprintf(repairGate,sizeof(repairGate),"%sgpu-repair.enable",Root);
    RepairEnabled=GetFileAttributesA(repairGate)!=INVALID_FILE_ATTRIBUTES;
    ID3D11Device* dev=nullptr;ID3D11DeviceContext* ctx=nullptr;
    HRESULT hr=D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&dev,nullptr,&ctx);
    fprintf(Log,"{\"event\":\"device\",\"hr\":%ld}\n",(long)hr);fflush(Log);
    bool enabled=false;
    if(SUCCEEDED(hr) && MH_Initialize()==MH_OK) {
        void** v=*reinterpret_cast<void***>(ctx);
        struct H {int i;void* replacement;void** original;} hooks[]={
            {12,(void*)HookDI,(void**)&OrigDI},{13,(void*)HookD,(void**)&OrigD},
            {20,(void*)HookDII,(void**)&OrigDII},{21,(void*)HookDI2,(void**)&OrigDI2},
            {39,(void*)HookDIII,(void**)&OrigDIII},{40,(void*)HookDII2,(void**)&OrigDII2}};
        bool ok=true;
        for(auto& h:hooks){auto s=MH_CreateHook(v[h.i],h.replacement,h.original);fprintf(Log,"{\"event\":\"hook\",\"index\":%d,\"status\":%d}\n",h.i,(int)s);if(s!=MH_OK)ok=false;}
        if(ok && MH_EnableHook(MH_ALL_HOOKS)==MH_OK) enabled=true;
        fflush(Log);
        if(enabled)for(int i=0;i<300 && (RepairEnabled || Captures<128);i++){if(GetFileAttributesA(gate)==INVALID_FILE_ATTRIBUTES)break;Sleep(50);}
        InterlockedExchange(&Stopping,1);MH_DisableHook(MH_ALL_HOOKS);Sleep(100);
        // A graphics callback can be waiting for the GPU. Never unload its code while active.
        for(int i=0;i<200 && Active;i++)Sleep(10);
        if(Active){fprintf(Log,"{\"event\":\"unload-deferred-active-callback\"}\n");fflush(Log);return 2;}
        MH_Uninitialize();
    }
    for(UINT i=0;i<RepairCount;i++){Release(Repairs[i].view);Release(Repairs[i].source);}
    fprintf(Log,"{\"event\":\"restored\",\"captures\":%ld,\"hooksEnabled\":%s}\n",Captures,enabled?"true":"false");fclose(Log);Log=nullptr;
    Release(ctx);Release(dev);DeleteCriticalSection(&Lock);FreeLibraryAndExitThread(Module,0);return 0;
}
BOOL WINAPI DllMain(HINSTANCE module,DWORD reason,LPVOID) {
    if(reason==DLL_PROCESS_ATTACH){Module=module;DisableThreadLibraryCalls(module);HANDLE t=CreateThread(nullptr,0,Worker,nullptr,0,nullptr);if(t)CloseHandle(t);}
    return TRUE;
}

"""Native vanilla baseline and six independent single-change experiments."""
import json, shutil, sys, time
from pathlib import Path
import game
import shapes_fixture as capture

ROOT=Path(__file__).resolve().parents[2]/'artifacts/flag-vanilla-steps-20261021'
game.destination=ROOT
capture.ROOT=ROOT
call=game.call
MODELS=['Flag16m']+[r'TMIS_VanillaSteps\TMIS_Step'+s+'.Item.Gbx' for s in
    ['01_ZeroFids','02_Roundtrip','03_Name','04_Attachment025','05_Width1025','06_Long250','07_Swallowtail']]

def setup():
    assert call('GetMode')['mode']=='MapEditor'
    assert call('GetMapInfo')['nbItems']==0
    for i in range(30):
        found=call('tm-mcp-pack-epp.FindInventory',{'query':'TMIS_Step0','limit':20})
        if found['count']==7:break
        time.sleep(1)
    else:raise RuntimeError(found)
    call('tm-mcp-pack-epp.SetAgentTag',{'tag':'tmis:vanilla-steps-20261021'})
    for i,model in enumerate(MODELS):
        result=call('tm-mcp-pack-epp.PlaceItem',{'itemPath':model,'x':1340+20*i,'y':8,'z':1360,'yaw':1.57079632679,'autofocus':False})
        assert result['afterItems']==i+1
    items=call('GetItems',{'limit':20})
    assert [p['idName'] for p in items['items']]==MODELS,items
    (ROOT/'placed-steps.json').write_text(json.dumps(items,indent=2))
    call('ControlOverlay',{'action':'hide'})
    call('ControlCamera',{'action':'look','direction':'North','smooth':False})
    call('ControlCamera',{'action':'setVStep','step':'Low','smooth':False})
    for label,x,d in [('all',1410,130),('baseline',1360,55),('geometry',1460,60)]:
        call('SetEditorCamera',{'x':x,'y':20,'z':1360,'distance':d})
        time.sleep(.4)
        print(capture.shot('evidence/steps-'+label),flush=True)

if __name__=='__main__':
    if sys.argv[1]=='setup':setup()
    elif sys.argv[1]=='record':capture.record(sys.argv[2],sys.argv[3],float(sys.argv[4]) if len(sys.argv)>4 else 20)

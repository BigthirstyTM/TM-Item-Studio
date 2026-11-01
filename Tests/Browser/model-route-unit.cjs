const assert=require('node:assert/strict');
const {create,simplify}=require('../../wwwroot/js/modelRoute.js');
// A U shaped strip: the solver must take the connected detour, not cross air.
const positions=[0,0,0, 1,0,0, 0,0,5, 1,0,5, 4,0,5, 5,0,5, 4,0,0, 5,0,0];
const indices=[0,1,2,1,3,2,2,3,4,3,5,4,4,5,6,5,7,6];
const graph=create(positions,indices),route=graph.between([0,0,0],[5,0,0]);
assert.ok(route.some(p=>p[2]===5),'route crossed the empty gap');
assert.deepEqual(route[0],[0,0,0]);assert.deepEqual(route.at(-1),[5,0,0]);
const split=create([...positions,100,0,0,101,0,0,100,0,1],[...indices,8,9,10]);
assert.throws(()=>split.between([0,0,0],[100,0,0]),/disconnected/);
const welded=create([0,0,0,1,0,0,0,0,1, 1,0,0,1,0,1,0,0,1],[0,1,2,3,4,5]);
assert.equal(welded.vertices.length,4);assert.ok(welded.between([0,0,0],[1,0,1]).length>=2);
assert.deepEqual(simplify([[0,0,0],[1,0,0],[2,0,0]],0),[[0,0,0],[2,0,0]]);
assert.equal(simplify([[0,0,0],[1,1,0],[2,0,0]],.1).length,3);
assert.throws(()=>create([NaN,0,0],[0,0,0]),/Invalid/);
console.log('PASS mesh routing: connected detour, disconnected refusal, seam welding, bounded simplification and invalid input');

// Mesh-edge routing: disconnected surfaces remain disconnected. No GBX mutation.
(function(root) {
    function create(positions, indices) {
        if(!positions.length || positions.length%3 || !indices.length || indices.length%3 || indices.some(i=>!Number.isInteger(i)||i<0))throw Error("Invalid reference buffers.");
        if (positions.length > 300000 || indices.length > 600000) throw Error('Reference limit: 100,000 vertices / 200,000 triangles per part. Choose a smaller part.');
        const vertices=[], edges=[], remap=[], lookup=new Map();
        for(let i=0;i<positions.length;i+=3) {
            const p=positions.slice(i,i+3);if(!p.every(Number.isFinite))throw Error('Invalid reference coordinates.');
            const key=p.map(v=>Math.round(v*10000)).join(',');let id=lookup.get(key);
            if(id===undefined){id=vertices.length;lookup.set(key,id);vertices.push(p);edges.push(new Map());}remap.push(id);
        }
        const distance=(a,b)=>Math.hypot(...a.map((v,i)=>v-b[i]));
        for(let i=0;i<indices.length;i+=3)for(let j=0;j<3;j++) {
            const a=remap[indices[i+j]],b=remap[indices[i+(j+1)%3]];
            if(a===undefined||b===undefined)throw Error('Invalid reference triangle.');
            if(a!==b){const d=distance(vertices[a],vertices[b]);edges[a].set(b,d);edges[b].set(a,d);}
        }
        function nearest(p){let best=-1,d=Infinity;vertices.forEach((v,i)=>{const n=distance(v,p);if(n<d){d=n;best=i;}});return best;}
        function between(start,end) {
            const a=nearest(start),b=nearest(end);if(a<0||b<0)throw Error('Empty reference mesh.');
            const costs=new Float64Array(vertices.length).fill(Infinity),prev=new Int32Array(vertices.length).fill(-1),heap=[];
            function push(id,cost){let i=heap.length;heap.push([id,cost]);while(i){const p=(i-1)>>1;if(heap[p][1]<=cost)break;heap[i]=heap[p];i=p;}heap[i]=[id,cost];}
            function pop(){const top=heap[0],last=heap.pop();if(heap.length){let i=0;while(i*2+1<heap.length){let c=i*2+1;if(c+1<heap.length&&heap[c+1][1]<heap[c][1])c++;if(heap[c][1]>=last[1])break;heap[i]=heap[c];i=c;}heap[i]=last;}return top;}
            costs[a]=0;push(a,0);
            while(heap.length){const [id,cost]=pop();if(cost!==costs[id])continue;if(id===b)break;for(const [next,length] of edges[id])if(cost+length<costs[next]){costs[next]=cost+length;prev[next]=id;push(next,costs[next]);}}
            if(!Number.isFinite(costs[b]))throw Error('These guide points are on disconnected surfaces. Choose a connected rail/deck part or add a manual bridge.');
            const route=[];for(let id=b;id>=0;id=prev[id]){route.push(vertices[id]);if(id===a)break;}return route.reverse();
        }
        return {vertices,between};
    }
    function simplify(points,tolerance) {
        if(points.length<3)return points;
        const keep=new Set([0,points.length-1]),stack=[[0,points.length-1]];
        while(stack.length){const [a,b]=stack.pop(),ab=points[b].map((v,k)=>v-points[a][k]),len=ab.reduce((s,v)=>s+v*v,0);let max=tolerance,index=-1;
            for(let i=a+1;i<b;i++){const ap=points[i].map((v,k)=>v-points[a][k]),t=len?Math.max(0,Math.min(1,ap.reduce((s,v,k)=>s+v*ab[k],0)/len)):0;const d=Math.hypot(...ap.map((v,k)=>v-t*ab[k]));if(d>max){max=d;index=i;}}
            if(index>=0){keep.add(index);stack.push([a,index],[index,b]);}}
        return [...keep].sort((a,b)=>a-b).map(i=>points[i]);
    }
    const api={create,simplify};root.ModelRoute=api;if(typeof module!=='undefined')module.exports=api;
})(globalThis);

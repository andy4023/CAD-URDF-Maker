// Test-only native-bridge stub. The production editor does not load this script.
const listeners=[];
let fixture=null;
const fixtureMath=import('/web/math.js'+new URL(document.querySelector('link[rel="stylesheet"]').href).search);
async function publishFixture(){
  const math=await fixtureMath,p=fixture.project,properties={},originErrors={};
  for(const link of p.Links){
    link.Origin=link.Origin||draftOrigin();link.ShowFrame=!!link.ShowFrame;originErrors[link.Id]=originError(p,link,math);
    if(originErrors[link.Id]||!link.Origin.Confirmed)continue;
    const parts=link.Components.map(id=>p.Components.find(c=>c.Id===id)).filter(c=>c&&!c.Excluded).map(c=>{
      const t=math.multiply(math.inverse(math.linkFrame(p,link)),c.ZeroTransform),source=c.Override.Mode==='complete'?c.Override.Complete:c.Cad;
      const tensor=Array(9).fill(0);for(let i=0;i<3;i++)for(let j=0;j<3;j++)for(let k=0;k<3;k++)for(let l=0;l<3;l++)tensor[i*3+j]+=t[i*4+k]*source.Inertia[k*3+l]*t[j*4+l];
      return{Mass:source.Mass,Cog:math.point(t,source.Cog),Inertia:tensor};
    });
    const mass=parts.reduce((sum,c)=>sum+c.Mass,0);if(mass<=0)continue;
    const cog=[0,1,2].map(i=>parts.reduce((sum,c)=>sum+c.Mass*c.Cog[i],0)/mass),inertia=Array(9).fill(0);
    for(const c of parts){const d=c.Cog.map((v,i)=>v-cog[i]),d2=d.reduce((sum,v)=>sum+v*v,0);for(let i=0;i<3;i++)for(let j=0;j<3;j++)inertia[i*3+j]+=c.Inertia[i*3+j]+c.Mass*((i===j?d2:0)-d[i]*d[j]);}
    properties[link.Id]={Mass:mass,Cog:cog,Inertia:inertia,Source:'Procedural analytical fixture — not native CAD verification'};
  }
  fixture.properties=properties;fixture.originErrors=originErrors;listeners.forEach(cb=>cb({data:structuredClone(fixture)}));
}
function draftOrigin(){return{Mode:'draft',Confirmed:false,ReferenceValid:true,JointId:'',PointReference:'',PointComponent:'',LocalPoint:null,SourcePoint:null,AxisPoint:null,AxisDirection:null,OffsetMetres:0};}
function projection(q,a,d,offset=0){const n=Math.hypot(...d);if(n<1e-12)throw new Error('Axis must be nonzero.');d=d.map(x=>x/n);const t=q.reduce((sum,v,i)=>sum+(v-a[i])*d[i],offset);return a.map((v,i)=>v+t*d[i]);}
function originError(p,link,math){const o=link.Origin,j=p.Joints.find(j=>j.Child===link.Id);if(o.Mode==='draft'||!o.SourcePoint)return 'Define and confirm this link origin first.';if(!o.ReferenceValid)return 'Origin reference needs review.';if(link.Id!==p.RootLink&&!j)return 'Choose the parent joint first.';if(link.Id!==p.RootLink&&j.Type!=='fixed'){if(!o.AxisDirection)return 'Define the joint axis first.';const projected=projection([link.ZeroFrame[3],link.ZeroFrame[7],link.ZeroFrame[11]],o.AxisPoint,o.AxisDirection);if(Math.hypot(...projected.map((x,i)=>x-link.ZeroFrame[i*4+3]))>1e-6)return 'Origin must lie on the joint axis.';}return null;}
async function originCommand(message){
  const math=await fixtureMath,p=fixture.project,link=p.Links.find(l=>l.Id===message.id),o=link.Origin,j=p.Joints.find(j=>j.Child===link.Id),moving=link.Id!==p.RootLink&&j&&j.Type!=='fixed';
  const input=link.Id===p.RootLink?math.identity():math.linkFrame(p,p.Links.find(l=>l.Id===j?.Parent));
  const invalidate=()=>{o.Confirmed=false;if(j)j.Confirmed=false;link.ShowFrame=true;};
  const position=(q,mode)=>{let target=q;if(moving){if(!o.AxisDirection)throw new Error('Define the joint axis first.');target=projection(q,o.AxisPoint,o.AxisDirection,o.OffsetMetres);if(mode==='numeric'&&Math.hypot(...projection(q,o.AxisPoint,o.AxisDirection).map((x,i)=>x-q[i]))>1e-6)throw new Error('Origin is more than 1 µm away from the joint axis.');}o.SourcePoint=q;o.Mode=mode;o.ReferenceValid=true;o.JointId=j?.Id||'';[link.ZeroFrame[3],link.ZeroFrame[7],link.ZeroFrame[11]]=target;invalidate();};
  if(message.type==='origin-begin')invalidate();
  else if(message.type==='origin-axis'||message.type==='origin-numeric-axis'){
    if(!moving)throw new Error('Choose a moving parent joint first.');o.AxisPoint=message.type==='origin-axis'?[link.ZeroFrame[3],link.ZeroFrame[7],link.ZeroFrame[11]]:math.point(input,message.point);o.AxisDirection=math.normalize(message.type==='origin-axis'?math.vector(link.ZeroFrame,j.Axis):math.vector(input,message.direction));o.JointId=j.Id;j.Axis=math.normalize(math.vector(math.inverse(link.ZeroFrame),o.AxisDirection));invalidate();
  }else if(message.type==='origin-point'){
    // Deliberately simulated selection: native vertex picking is verified separately.
    const q=[link.ZeroFrame[3]+.002,link.ZeroFrame[7]+.003,link.ZeroFrame[11]+.004];position(q,'cad-point');o.PointReference='fixture-vertex';o.PointComponent=link.Components[0];o.LocalPoint=q;
  }else if(message.type==='origin-position'){o.OffsetMetres=0;position(math.point(input,message.point),'numeric');o.PointReference='';o.LocalPoint=null;}
  else if(message.type==='origin-offset'){if(!moving||!o.SourcePoint)throw new Error('Define the axis and origin point first.');o.OffsetMetres=message.offsetMetres;const target=projection(o.SourcePoint,o.AxisPoint,o.AxisDirection,o.OffsetMetres);[link.ZeroFrame[3],link.ZeroFrame[7],link.ZeroFrame[11]]=target;invalidate();}
  else if(message.type==='origin-confirm'){const error=originError(p,link,math);if(error)throw new Error(error);o.Confirmed=true;}
}
window.chrome=window.chrome||{};
window.chrome.webview={
  addEventListener(name,callback){if(name==='message')listeners.push(callback);},
  async postMessage(message){
    if(message.type==='ready'){
      fixture=await(await fetch('/build/synthetic-viewer-fixture.json')).json();
      if(new URLSearchParams(location.search).has('scope')){
        const source=fixture.project.Components[0];for(const [i,name] of ['Unused mounting bracket','Unused camera housing','Unused cable clip'].entries()){
          const extra=structuredClone(source);extra.Id='unused-'+i;extra.Name=name;extra.Excluded=false;fixture.project.Components.push(extra);fixture.meshUrls[extra.Id]=fixture.meshUrls[source.Id];
        }
      }
      await publishFixture();
    }
    else if(message.type==='update'){
      for(const link of message.project.Links){const old=fixture.project.Links.find(l=>l.Id===link.Id);link.Origin=old?.Origin||draftOrigin();const before=fixture.project.Joints.find(j=>j.Child===link.Id),after=message.project.Joints.find(j=>j.Child===link.Id);if(old&&(JSON.stringify(old.ZeroFrame)!==JSON.stringify(link.ZeroFrame)||JSON.stringify(before?.Axis)!==JSON.stringify(after?.Axis)||before?.Type!==after?.Type||before?.Parent!==after?.Parent))link.Origin.Confirmed=false;}
      fixture.project=message.project;await publishFixture();
    }
    else if(message.type.startsWith('origin-')){try{await originCommand(message);await publishFixture();}catch(error){listeners.forEach(cb=>cb({data:{type:'error',message:error.message}}));}}
    else if(['remove-unassigned','restore-components','remove-unassigned-cad'].includes(message.type)){
      // CAD selection is simulated only in this fixture.
      try{
        const p=fixture.project,ids=message.type==='remove-unassigned-cad'?['unused-2']:message.ids;
        if(!ids?.length)throw new Error('Select at least one unassigned component.');
        const components=ids.map(id=>p.Components.find(c=>c.Id===id));
        if(components.some(c=>!c||p.Links.some(l=>l.Components.includes(c.Id))))throw new Error('Select only unassigned components.');
        components.forEach(c=>c.Excluded=message.type!=='restore-components');await publishFixture();
        listeners.forEach(cb=>cb({data:{type:'notice',message:components.length+' component(s) '+(message.type==='restore-components'?'restored as unassigned.':'removed from the URDF. CAD is unchanged.')}}));
      }catch(error){listeners.forEach(cb=>cb({data:{type:'error',message:error.message}}));}
    }
    else if(message.type==='validate'||message.type==='export'){
      const diagnostics=fixture.project.Components.filter(c=>!c.Excluded&&!fixture.project.Links.some(l=>l.Components.includes(c.Id))).map(c=>({Code:'membership',Target:c.Name,Message:'Unassigned component. Assign it to a link or explicitly remove it from the URDF.',Severity:'error'}));
      listeners.forEach(cb=>cb({data:{type:'validation',diagnostics}}));
    }
    else listeners.forEach(cb=>cb({data:{type:'notice',message:'Viewer fixture only — use the SolidWorks add-in for '+message.type+'.'}}));
  }
};

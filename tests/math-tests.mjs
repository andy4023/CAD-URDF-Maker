import assert from 'node:assert/strict';
import { identity, multiply, inverse, point, vector, fromRpy, rpy, motion, forward, symmetricEigen, ellipsoid, axesAlignment, alignmentTransform, linkFrame, jointAxis, storedAxis } from '../web/math.js';
const near=(a,b,t=1e-10)=>assert.ok(Math.abs(a-b)<t,`${a} != ${b}`);
const matrix=fromRpy([.1,.2,.3],[.4,-.5,.6]);
multiply(matrix,inverse(matrix)).forEach((n,i)=>near(n,identity()[i]));
rpy(matrix).forEach((n,i)=>near(n,[.4,-.5,.6][i]));
const j={Id:'j',Parent:'root',Child:'child',Type:'revolute',Axis:[0,0,1]};
let child=identity();child[3]=1;
const project={Links:[{Id:'root',ZeroFrame:identity()},{Id:'child',ZeroFrame:child}],Joints:[j]};
let frames=forward(project,{j:Math.PI/2});near(frames.child[3],1);const p=point(frames.child,[1,0,0]);near(p[0],1);near(p[1],1);
j.Type='prismatic';frames=forward(project,{j:.3});near(frames.child[11],.3);
j.Type='fixed';frames=forward(project,{j:20});near(frames.child[11],0);
const e=symmetricEigen([2,1,0,1,3,0,0,0,4]);const sorted=e.values.slice().sort((a,b)=>a-b);near(sorted[0],(5-Math.sqrt(5))/2);near(sorted[2],4);
const ell=ellipsoid({Mass:12,Cog:[0,0,0],Inertia:[13,0,0,0,10,0,0,0,5]});near(ell.radii[0],Math.sqrt(5/12));
const cycle={Links:project.Links,Joints:[j,{Id:'back',Parent:'child',Child:'root',Type:'fixed',Axis:[0,0,1]}]};assert.throws(()=>forward(cycle,{}),/cycle/);
assert.throws(()=>motion({...j,Type:'revolute',Axis:[0,0,0]},1),/Axis/);
console.log('PASS browser math: transforms, RPY, revolute/prismatic/fixed motion, tensor eigenvectors, inertia ellipsoid, cycle rejection');
const directions=[[1,0,0],[-1,0,0],[0,1,0],[0,-1,0],[0,0,1],[0,0,-1]];
for(const up of directions)for(const fwd of directions){if(up.reduce((n,v,i)=>n+v*fwd[i],0)!==0){assert.throws(()=>axesAlignment(up,fwd),/different/);continue;}const rotation=axesAlignment(up,fwd);vector(rotation,up).forEach((v,i)=>near(v,[0,0,1][i]));vector(rotation,fwd).forEach((v,i)=>near(v,[1,0,0][i]));const roundtrip=fromRpy([0,0,0],rpy(rotation));rotation.forEach((v,i)=>near(v,roundtrip[i]));}
const rootFrame=fromRpy([2,3,4],[.2,-.3,.4]),childFrame=fromRpy([2,4,4],[0,0,0]);
const aligned={ChildFramesFollowRoot:false,RootLink:'root',Alignment:{Enabled:true,Rpy:[Math.PI/2,0,0]},Links:[{Id:'root',ZeroFrame:rootFrame},{Id:'child',ZeroFrame:childFrame}],Joints:[{Id:'joint',Parent:'root',Child:'child',Type:'revolute',Axis:[0,0,1]}]};
let alignedFrames=forward(aligned,{});alignedFrames.root.forEach((v,i)=>near(v,identity()[i]));near(alignedFrames.child[11],1);vector(alignedFrames.child,[0,0,1]).forEach((v,i)=>near(v,[0,-1,0][i]));
alignedFrames=forward(aligned,{joint:Math.PI/2});const movingPoint=point(alignedFrames.child,[1,0,0]);near(movingPoint[0],0);near(movingPoint[1],0);near(movingPoint[2],2);
const cadToRobot=alignmentTransform(aligned),originalComponent=fromRpy([2,3,4],[0,0,0]);const assembledRoot=multiply(multiply(forward(aligned,{}).root,inverse(linkFrame(aligned,aligned.Links[0]))),originalComponent);assembledRoot.forEach((v,i)=>near(v,multiply(cadToRobot,originalComponent)[i]));
aligned.Alignment.Enabled=false;forward(aligned,{}).root.forEach((v,i)=>near(v,rootFrame[i]));assert.deepEqual(aligned.Links[0].ZeroFrame,rootFrame);
console.log('PASS alignment: 24 right-handed CAD axis mappings, gimbal rotations, root frame, child motion, component placement and reset');
const following={RootLink:'root',ChildFramesFollowRoot:true,Alignment:{Enabled:true,Rpy:[.31,-.23,.41]},Links:[{Id:'root',ZeroFrame:fromRpy([1,2,3],[.1,-.2,.3])},{Id:'child',ZeroFrame:fromRpy([1.2,2.4,3.6],[.6,-.7,.8])},{Id:'grand',ZeroFrame:fromRpy([1.4,2.8,4.2],[-.4,.9,-.6])}],Joints:[{Id:'hinge',Parent:'root',Child:'child',Type:'revolute',Axis:[0,0,1]},{Id:'slide',Parent:'child',Child:'grand',Type:'prismatic',Axis:[1,0,0]}]};
const captured=JSON.stringify(following.Links),stored=JSON.stringify(following.Joints);
for(const pose of [{},{hinge:.73,slide:.12},{hinge:-.53,slide:-.08}]){
 following.ChildFramesFollowRoot=false;const old=forward(following,pose);
 following.ChildFramesFollowRoot=true;const next=forward(following,pose);
 for(const link of following.Links){const oldPlacement=multiply(old[link.Id],inverse(link.ZeroFrame));const nextPlacement=multiply(next[link.Id],inverse(linkFrame(following,link)));if(link.Id==='root')continue;oldPlacement.forEach((v,i)=>near(v,nextPlacement[i]));}
}
const zero=forward(following,{});for(const id of ['child','grand'])for(const i of [0,1,2,4,5,6,8,9,10])near(zero[id][i],zero.root[i]);
const desired=[0,1,0];const edited={...following.Joints[0],Axis:storedAxis(following,'child',desired)};jointAxis(following,edited).forEach((v,i)=>near(v,desired[i]));
for(const type of ['fixed','continuous']){following.Joints[0].Type=type;following.ChildFramesFollowRoot=false;const old=forward(following,{hinge:1.2,slide:.04});following.ChildFramesFollowRoot=true;const next=forward(following,{hinge:1.2,slide:.04});const oldGeometry=multiply(old.grand,inverse(following.Links[2].ZeroFrame)),newGeometry=multiply(next.grand,inverse(linkFrame(following,following.Links[2])));oldGeometry.forEach((v,i)=>near(v,newGeometry[i]));}
assert.equal(JSON.stringify(following.Links),captured);following.Joints[0].Type='revolute';assert.equal(JSON.stringify(following.Joints),stored);
console.log('PASS root-oriented child frames: parallel zero frames, unchanged multi-joint motion for all four joint types, reversible policy and editable axis conversion');
const originShiftPose={hinge:.73,slide:.12},oldGeometry={};
for(const link of following.Links)oldGeometry[link.Id]=multiply(forward(following,originShiftPose)[link.Id],inverse(linkFrame(following,link)));
const shiftDirection=vector(following.Links[1].ZeroFrame,following.Joints[0].Axis);for(let i=0;i<3;i++)following.Links[1].ZeroFrame[i*4+3]+=.17*shiftDirection[i];
const shifted=forward(following,originShiftPose);for(const link of following.Links){const geometry=multiply(shifted[link.Id],inverse(linkFrame(following,link)));oldGeometry[link.Id].forEach((v,i)=>near(v,geometry[i]));}
console.log('PASS origin shift along joint axis preserves component motion through the descendant chain');

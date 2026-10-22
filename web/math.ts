import type { Project, JointRecord, Physics } from './types.js';
export const identity = (): number[] => [1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
export function multiply(a: number[], b: number[]): number[] { const c = new Array(16).fill(0); for (let i=0;i<4;i++) for(let j=0;j<4;j++) for(let k=0;k<4;k++) c[i*4+j]+=a[i*4+k]*b[k*4+j];return c; }
export function inverse(a: number[]): number[] { const b=identity(); for(let i=0;i<3;i++)for(let j=0;j<3;j++)b[i*4+j]=a[j*4+i];for(let i=0;i<3;i++)b[i*4+3]=-(b[i*4]*a[3]+b[i*4+1]*a[7]+b[i*4+2]*a[11]);return b; }
export function point(t: number[],p: number[]): number[] { return [0,1,2].map(i=>t[i*4]*p[0]+t[i*4+1]*p[1]+t[i*4+2]*p[2]+t[i*4+3]); }
export function vector(t: number[],p: number[]): number[] { return [0,1,2].map(i=>t[i*4]*p[0]+t[i*4+1]*p[1]+t[i*4+2]*p[2]); }
export function normalize(a: number[]): number[] { const length=Math.hypot(...a);if(!Number.isFinite(length)||length<1e-12)throw new Error('Axis needs a nonzero finite length.');return a.map(v=>v/length); }
export function fromRpy(xyz: number[],rpy: number[]): number[] { const [r,p,y]=rpy;const cr=Math.cos(r),sr=Math.sin(r),cp=Math.cos(p),sp=Math.sin(p),cy=Math.cos(y),sy=Math.sin(y);return [cy*cp,cy*sp*sr-sy*cr,cy*sp*cr+sy*sr,xyz[0],sy*cp,sy*sp*sr+cy*cr,sy*sp*cr-cy*sr,xyz[1],-sp,cp*sr,cp*cr,xyz[2],0,0,0,1]; }
export function rpy(t: number[]): number[] { const p=Math.asin(Math.max(-1,Math.min(1,-t[8])));return Math.abs(Math.cos(p))>1e-9?[Math.atan2(t[9],t[10]),p,Math.atan2(t[4],t[0])]:[0,p,Math.atan2(-t[1],t[5])]; }
export function linkFrame(project: Project, link: Project['Links'][number]): number[] {
    if(project.ChildFramesFollowRoot!==false&&link.Id!==project.RootLink){const root=project.Links.find(l=>l.Id===project.RootLink);if(root){const frame=linkFrame(project,root).slice();frame[3]=link.ZeroFrame[3];frame[7]=link.ZeroFrame[7];frame[11]=link.ZeroFrame[11];return frame;}}
    if(!project.Alignment?.Enabled||link.Id!==project.RootLink)return link.ZeroFrame;
    const frame=inverse(fromRpy([0,0,0],project.Alignment.Rpy));
    frame[3]=link.ZeroFrame[3];frame[7]=link.ZeroFrame[7];frame[11]=link.ZeroFrame[11];return frame;
}
export function jointAxis(project: Project, joint: JointRecord): number[] {
    const child=project.Links.find(l=>l.Id===joint.Child);if(!child)throw new Error('Missing child link');
    return normalize(vector(inverse(linkFrame(project,child)),vector(child.ZeroFrame,joint.Axis)));
}
export function storedAxis(project: Project, childId: string, axis: number[]): number[] {
    const child=project.Links.find(l=>l.Id===childId);if(!child)throw new Error('Missing child link');
    return normalize(vector(inverse(child.ZeroFrame),vector(linkFrame(project,child),axis)));
}
export function alignmentTransform(project: Project): number[] {
    if(!project.Alignment?.Enabled)return identity();
    const t=fromRpy([0,0,0],project.Alignment.Rpy),root=project.Links.find(l=>l.Id===project.RootLink);
    if(root){const origin=vector(t,[root.ZeroFrame[3],root.ZeroFrame[7],root.ZeroFrame[11]]);t[3]=-origin[0];t[7]=-origin[1];t[11]=-origin[2];}return t;
}
export function axesAlignment(up: number[], forward: number[]): number[] {
    const z=normalize(up),x=normalize(forward);if(Math.abs(z.reduce((sum,n,i)=>sum+n*x[i],0))>1e-8)throw new Error('Up and forward must use different CAD axes.');
    const y=[z[1]*x[2]-z[2]*x[1],z[2]*x[0]-z[0]*x[2],z[0]*x[1]-z[1]*x[0]];
    return [x[0],x[1],x[2],0,y[0],y[1],y[2],0,z[0],z[1],z[2],0,0,0,0,1];
}
export function motion(joint: JointRecord,value: number): number[] { const t=identity();if(joint.Type==='fixed')return t;const [x,y,z]=normalize(joint.Axis);if(joint.Type==='prismatic'){t[3]=x*value;t[7]=y*value;t[11]=z*value;return t;}const c=Math.cos(value),s=Math.sin(value),v=1-c;return [x*x*v+c,x*y*v-z*s,x*z*v+y*s,0,y*x*v+z*s,y*y*v+c,y*z*v-x*s,0,z*x*v-y*s,z*y*v+x*s,z*z*v+c,0,0,0,0,1]; }
export function forward(project: Project, positions: Record<string,number>): Record<string,number[]> {
    const frames: Record<string,number[]>={};const links=new Map(project.Links.map(l=>[l.Id,l]));const active=new Set<string>();
    const solve=(id:string):number[]=>{if(frames[id])return frames[id];if(active.has(id))throw new Error('Joint cycle');active.add(id);const link=links.get(id);if(!link)throw new Error('Missing link');const joint=project.Joints.find(j=>j.Child===id);let result=linkFrame(project,link);if(joint&&links.has(joint.Parent)){const parent=links.get(joint.Parent);result=multiply(multiply(solve(parent.Id),multiply(inverse(linkFrame(project,parent)),linkFrame(project,link))),motion({...joint,Axis:joint.Type==='fixed'?joint.Axis:jointAxis(project,joint)},positions[joint.Id]||0));}active.delete(id);return frames[id]=result;};
    for(const link of project.Links)solve(link.Id);const alignment=alignmentTransform(project);for(const id of Object.keys(frames))frames[id]=multiply(alignment,frames[id]);return frames;
}
export function symmetricEigen(tensor: number[]): { values: number[]; basis: number[] } {
    let a=tensor.slice(),basis=[1,0,0,0,1,0,0,0,1];for(let sweep=0;sweep<32;sweep++){let p=0,q=1;if(Math.abs(a[2])>Math.abs(a[1]))q=2;if(Math.abs(a[5])>Math.abs(a[p*3+q])){p=1;q=2;}if(Math.abs(a[p*3+q])<1e-16)break;const angle=.5*Math.atan2(2*a[p*3+q],a[q*3+q]-a[p*3+p]);const c=Math.cos(angle),s=Math.sin(angle),r=[1,0,0,0,1,0,0,0,1];r[p*3+p]=r[q*3+q]=c;r[p*3+q]=s;r[q*3+p]=-s;const next=new Array(9).fill(0),vectors=new Array(9).fill(0);for(let i=0;i<3;i++)for(let j=0;j<3;j++){for(let k=0;k<3;k++){vectors[i*3+j]+=basis[i*3+k]*r[k*3+j];for(let l=0;l<3;l++)next[i*3+j]+=r[k*3+i]*a[k*3+l]*r[l*3+j];}}a=next;basis=vectors;}return{values:[a[0],a[4],a[8]],basis};
}
export function ellipsoid(p: Physics): { radii: number[]; basis: number[] } { const e=symmetricEigen(p.Inertia);const [a,b,c]=e.values;return{radii:[b+c-a,a+c-b,a+b-c].map(x=>Math.sqrt(Math.max(0,5*x/(2*p.Mass)))),basis:e.basis}; }

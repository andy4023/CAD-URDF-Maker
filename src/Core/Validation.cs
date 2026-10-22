using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CadUrdfMaker.Core
{
    public static class Validation
    {
        static bool Name(string s) { return !string.IsNullOrEmpty(s) && Regex.IsMatch(s,"^[A-Za-z_][A-Za-z0-9_]*$"); }
        public static string RobotNameError(string name)
        {
            if(!Name(name))return "Robot name must use letters, digits, and underscores, starting with a letter or underscore.";
            if(Regex.IsMatch(name,"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))return "This robot name is reserved by Windows. Choose another name for the export file and folder.";
            return null;
        }
        public static string SafeName(string s) { s=Regex.Replace(s??"link","[^A-Za-z0-9_]","_"); if(s.Length==0)s="link";if(char.IsDigit(s[0]))s="link_"+s;return s; }
        public static string PhysicsError(PhysicalProperties p)
        {
            if(p==null||!Math3.Finite(p.Mass)||p.Mass<=0)return "Mass must be positive and finite.";
            if(p.Cog==null||p.Cog.Length!=3||p.Cog.Any(x=>!Math3.Finite(x)))return "CoG requires three finite coordinates.";
            if(p.Inertia==null||p.Inertia.Length!=9||p.Inertia.Any(x=>!Math3.Finite(x)))return "Inertia requires a finite 3×3 tensor.";
            double tolerance=Math.Max(1e-14,p.Inertia.Select(Math.Abs).Max()*1e-9);
            for(int i=0;i<3;i++)for(int j=0;j<3;j++)if(Math.Abs(p.Inertia[i*3+j]-p.Inertia[j*3+i])>tolerance)return "Inertia tensor must be symmetric.";
            var e=Math3.Eigenvalues(p.Inertia);if(e[0]<=0)return "Inertia must be positive definite.";
            if(e[2]>e[0]+e[1]+tolerance)return string.Format(System.Globalization.CultureInfo.InvariantCulture,"Principal moments violate the physical triangle inequality: Imin={0:G9}, Imid={1:G9}, Imax={2:G9} kg·m²; excess={3:G9} kg·m². Imax must not exceed Imin + Imid. Compare SolidWorks principal moments and refresh CAD; review any physical override.",e[0],e[1],e[2],e[2]-e[0]-e[1]);
            return null;
        }
        public static List<Diagnostic> Check(RobotProject p,bool includeCad=true)
        {
            var d=new List<Diagnostic>();
            if(p.Alignment==null||p.Alignment.Rpy==null||p.Alignment.Rpy.Length!=3||p.Alignment.Rpy.Any(x=>!Math3.Finite(x)))d.Add(new Diagnostic("alignment","project","Alignment needs three finite roll, pitch, and yaw values."));
            if(p.SchemaVersion!=1)d.Add(new Diagnostic("schema","project","Unsupported project version."));
            string robotNameError=RobotNameError(p.Name);if(robotNameError!=null)d.Add(new Diagnostic("name","project",robotNameError));
            if(includeCad && (p.Stale||!p.ZeroCaptured))d.Add(new Diagnostic("stale","project","Capture the zero pose and refresh CAD data before export."));
            if(includeCad)foreach(string issue in p.CadIssues)d.Add(new Diagnostic("cad","assembly",issue));
            if(!Math3.Finite(p.Export.ChordMetres)||p.Export.ChordMetres<=0||!Math3.Finite(p.Export.AngleRadians)||p.Export.AngleRadians<=0||p.Export.AngleRadians>=Math.PI)d.Add(new Diagnostic("tessellation","project","Mesh tolerances must be positive; angular tolerance must be below 180°."));
            bool duplicateIds=p.Links.Select(x=>x.Id).Distinct().Count()!=p.Links.Count||p.Joints.Select(x=>x.Id).Distinct().Count()!=p.Joints.Count||p.Components.Select(x=>x.Id).Distinct().Count()!=p.Components.Count;
            if(duplicateIds){d.Add(new Diagnostic("duplicate_id","project","Duplicate IDs are not allowed."));return d;}
            if(p.Links.Count==0)d.Add(new Diagnostic("links","project","Create at least one link."));
            if(p.Links.Select(x=>x.Name).Distinct().Count()!=p.Links.Count||p.Joints.Select(x=>x.Name).Distinct().Count()!=p.Joints.Count)d.Add(new Diagnostic("duplicate_name","project","Link and joint names must each be unique."));
            var componentIds=new HashSet<string>(p.Components.Select(x=>x.Id));
            foreach(var c in p.Components)
            {
                int membership=p.Links.Sum(x=>x.Components.Count(id=>id==c.Id));
                if(c.Excluded){if(membership!=0)d.Add(new Diagnostic("membership",c.Name,"Excluded component remains assigned to a link."));continue;}
                if(membership!=1)d.Add(new Diagnostic("membership",c.Name,membership==0?"Unassigned component. Assign it to a link or explicitly remove it from the URDF.":"Assign this component to exactly one link."));
                if(includeCad && !c.Resolved)d.Add(new Diagnostic("unresolved",c.Name,"CAD component is unresolved or suppressed; refresh and remap, or explicitly exclude it."));
                if(!string.IsNullOrEmpty(c.Issue))d.Add(new Diagnostic("component",c.Name,c.Issue));
                if(!Math3.Rigid(c.ZeroTransform))d.Add(new Diagnostic("transform",c.Name,"Invalid component zero transform; scaled or mirrored instances require corrected CAD geometry."));
                if(c.Override==null){d.Add(new Diagnostic("override",c.Name,"Missing override record."));continue;}
                if(!new[]{"cad","density","complete"}.Contains(c.Override.Mode))d.Add(new Diagnostic("override",c.Name,"Unknown property override mode."));
                if(c.Override.Mode!="cad"&&string.IsNullOrWhiteSpace(c.Override.Note))d.Add(new Diagnostic("provenance",c.Name,"Add a source note for the property override."));
                if(c.Override.Mode=="cad"&&!c.MaterialKnown&&!c.MaterialAcknowledged)d.Add(new Diagnostic("material",c.Name,"CAD material/default density is unverified. Correct it or explicitly acknowledge the assumption."));
                if(c.Override.Mode=="density"&&(!Math3.Finite(c.Override.Density)||c.Override.Density<=0))d.Add(new Diagnostic("density",c.Name,"Density must be positive and finite."));
                try {string error=PhysicsError(Math3.Effective(c));if(error!=null)d.Add(new Diagnostic("physics",c.Name,error));}catch(Exception e){d.Add(new Diagnostic("physics",c.Name,e.Message));}
            }
            foreach(var l in p.Links)
            {
                if(!Name(l.Name))d.Add(new Diagnostic("name",l.Name,"Invalid link name."));
                if(!Math3.Rigid(l.ZeroFrame))d.Add(new Diagnostic("frame",l.Name,"Invalid link frame."));
                try{string originError=OriginWorkflow.Error(p,l);if(originError!=null)d.Add(new Diagnostic("origin",l.Name,originError));}catch(Exception){d.Add(new Diagnostic("origin",l.Name,"Invalid origin or joint-axis definition."));}
                if(l.Components.Count==0)d.Add(new Diagnostic("empty",l.Name,"Each link requires at least one component."));
                foreach(string id in l.Components)if(!componentIds.Contains(id))d.Add(new Diagnostic("missing_component",l.Name,"Link references a missing CAD component."));
            }
            var linkIds=new HashSet<string>(p.Links.Select(x=>x.Id));
            foreach(var j in p.Joints)
            {
                if(!Name(j.Name))d.Add(new Diagnostic("name",j.Name,"Invalid joint name."));
                if(!new[]{"fixed","revolute","continuous","prismatic"}.Contains(j.Type))d.Add(new Diagnostic("type",j.Name,"Unsupported joint type."));
                if(!linkIds.Contains(j.Parent)||!linkIds.Contains(j.Child)||j.Parent==j.Child)d.Add(new Diagnostic("joint_links",j.Name,"Choose distinct existing parent and child links."));
                if(!j.Confirmed)d.Add(new Diagnostic("confirm",j.Name,"Confirm this joint's frame, axis, and direction."));
                if(!j.ReferenceValid)d.Add(new Diagnostic("reference",j.Name,"Selected joint geometry changed or disappeared. Select and confirm it again."));
                if(j.Type!="fixed"){
                    if(j.Axis==null||j.Axis.Length!=3||j.Axis.Any(x=>!Math3.Finite(x))||Math.Abs(Math.Sqrt(Math3.Dot(j.Axis,j.Axis))-1)>1e-6)d.Add(new Diagnostic("axis",j.Name,"Axis must be a normalized finite vector in the child/joint frame."));
                    if(!Math3.Finite(j.Effort)||j.Effort<=0||!Math3.Finite(j.Velocity)||j.Velocity<=0)d.Add(new Diagnostic("limits",j.Name,"Supply positive effort and velocity limits."));
                    if(j.Type!="continuous"&&(!Math3.Finite(j.Lower)||!Math3.Finite(j.Upper)||j.Lower>j.Upper||j.Lower>0||j.Upper<0))d.Add(new Diagnostic("range",j.Name,"Limits must be finite, ordered, and include the captured zero pose."));
                }
            }
            if(!linkIds.Contains(p.RootLink))d.Add(new Diagnostic("root","project","Select a root link."));
            foreach(var l in p.Links){int n=p.Joints.Count(j=>j.Child==l.Id);if(n!=(l.Id==p.RootLink?0:1))d.Add(new Diagnostic("tree",l.Name,"Root must have no parent; every other link must have one parent."));}
            var visited=new HashSet<string>();var active=new HashSet<string>();Action<string> visit=null;visit=id=>{if(active.Contains(id)){d.Add(new Diagnostic("cycle",id,"Closed mechanisms must be resolved into a URDF tree."));return;}if(visited.Contains(id))return;visited.Add(id);active.Add(id);foreach(var j in p.Joints.Where(x=>x.Parent==id))visit(j.Child);active.Remove(id);};if(linkIds.Contains(p.RootLink))visit(p.RootLink);
            if(visited.Count!=p.Links.Count)d.Add(new Diagnostic("disconnected","project","All links must connect to the root."));
            return d;
        }
    }
}

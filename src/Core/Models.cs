using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace CadUrdfMaker.Core
{
    public sealed class PhysicalProperties
    {
        public double Mass { get; set; }
        public double[] Cog { get; set; }
        // Symmetric, row-major tensor about Cog. SI units, tensor (not product) signs.
        public double[] Inertia { get; set; }
        public string Source { get; set; }
        public PhysicalProperties() { Cog = new double[3]; Inertia = new double[9]; Source = "CAD"; }
    }
    public sealed class PropertyOverride
    {
        public string Mode { get; set; }
        public double Density { get; set; }
        public PhysicalProperties Complete { get; set; }
        public string Note { get; set; }
        public PropertyOverride() { Mode = "cad"; Complete = new PhysicalProperties(); Note = ""; }
    }
    public sealed class ComponentRecord
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Configuration { get; set; }
        public string PersistentReference { get; set; }
        public double[] ZeroTransform { get; set; }
        public PhysicalProperties Cad { get; set; }
        public PhysicalProperties UnitDensity { get; set; }
        public PropertyOverride Override { get; set; }
        public bool MaterialAcknowledged { get; set; }
        public bool MaterialKnown { get; set; }
        public bool Excluded { get; set; }
        public bool Hidden { get; set; }
        public bool Resolved { get; set; }
        public string Issue { get; set; }
        public ComponentRecord() { ZeroTransform = Math3.Identity(); Cad = new PhysicalProperties(); Override = new PropertyOverride(); Issue = ""; }
    }
    public sealed class OriginDefinition
    {
        public string Mode { get; set; }
        public bool Confirmed { get; set; }
        public bool ReferenceValid { get; set; }
        public string JointId { get; set; }
        public string PointReference { get; set; }
        public string PointComponent { get; set; }
        public double[] LocalPoint { get; set; }
        public double[] SourcePoint { get; set; }
        public double[] AxisPoint { get; set; }
        public double[] AxisDirection { get; set; }
        public double OffsetMetres { get; set; }
        public OriginDefinition() { Mode="draft";ReferenceValid=true;JointId="";PointReference="";PointComponent=""; }
    }
    public sealed class LinkRecord
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<string> Components { get; set; }
        public double[] ZeroFrame { get; set; }
        public OriginDefinition Origin { get; set; }
        public bool ShowFrame { get; set; }
        public LinkRecord() { Components = new List<string>(); ZeroFrame = Math3.Identity(); Origin=new OriginDefinition(); }
    }
    public sealed class JointRecord
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Parent { get; set; }
        public string Child { get; set; }
        public double[] Axis { get; set; }
        public double Lower { get; set; }
        public double Upper { get; set; }
        public double Effort { get; set; }
        public double Velocity { get; set; }
        public bool Confirmed { get; set; }
        public string GeometryReference { get; set; }
        public string GeometryComponent { get; set; }
        public double[] ReferencePoint { get; set; }
        public double[] ReferenceDirection { get; set; }
        public bool ReferenceValid { get; set; }
        public JointRecord() { Type = "revolute"; Axis = new double[] { 0, 0, 1 }; Lower = -Math.PI / 2; Upper = Math.PI / 2; GeometryReference = ""; ReferenceValid = true; }
    }
    public sealed class JointSuggestion
    {
        public string ComponentA { get; set; }
        public string ComponentB { get; set; }
        public string Type { get; set; }
        public string Reason { get; set; }
        public double[] Point { get; set; }
        public double[] Direction { get; set; }
    }
    public sealed class ExportSettings
    {
        public double ChordMetres { get; set; }
        public double AngleRadians { get; set; }
        public ExportSettings() { ChordMetres = 0.0001; AngleRadians = Math.PI / 36; }
    }
    public sealed class AlignmentSettings
    {
        public bool Enabled { get; set; }
        // Active rotation from captured CAD axes to robot axes: Rz(yaw) Ry(pitch) Rx(roll).
        public double[] Rpy { get; set; }
        public AlignmentSettings() { Rpy = new double[3]; }
    }
    public sealed class RobotProject
    {
        public int SchemaVersion { get; set; }
        public string Name { get; set; }
        public string AssemblyPath { get; set; }
        public string AssemblyTitle { get; set; }
        public string Configuration { get; set; }
        public string RootLink { get; set; }
        public string CapturedUtc { get; set; }
        public int CadStamp { get; set; }
        public bool Stale { get; set; }
        public bool ZeroCaptured { get; set; }
        public List<ComponentRecord> Components { get; set; }
        public List<LinkRecord> Links { get; set; }
        public List<JointRecord> Joints { get; set; }
        public List<JointSuggestion> Suggestions { get; set; }
        public List<string> CadIssues { get; set; }
        public ExportSettings Export { get; set; }
        public AlignmentSettings Alignment { get; set; }
        public bool ChildFramesFollowRoot { get; set; }
        public RobotProject() { SchemaVersion = 1; Name = "robot"; AssemblyPath = ""; AssemblyTitle = ""; Configuration = ""; RootLink = ""; Components = new List<ComponentRecord>(); Links = new List<LinkRecord>(); Joints = new List<JointRecord>(); Suggestions = new List<JointSuggestion>(); CadIssues = new List<string>(); Export = new ExportSettings(); Alignment = new AlignmentSettings(); ChildFramesFollowRoot = true; Stale = true; }
    }
    public sealed class Diagnostic
    {
        public string Severity { get; set; }
        public string Code { get; set; }
        public string Target { get; set; }
        public string Message { get; set; }
        public Diagnostic(string code, string target, string message, string severity = "error") { Code = code; Target = target; Message = message; Severity = severity; }
    }
    public static class ProjectIO
    {
        public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 100000000, RecursionLimit = 128 }; }
        public static string Json(object value) { return Serializer().Serialize(value); }
        public static RobotProject Parse(string json)
        {
            var p = Serializer().Deserialize<RobotProject>(json);
            if (p == null || p.SchemaVersion != 1 || p.Components == null || p.Links == null || p.Joints == null || p.Export == null || p.Suggestions == null || p.CadIssues == null) throw new InvalidDataException("Unsupported or incomplete CAD-URDF Maker project.");
            if(p.Alignment==null||p.Alignment.Rpy==null||p.Alignment.Rpy.Length!=3||Array.Exists(p.Alignment.Rpy,x=>!Math3.Finite(x)))throw new InvalidDataException("Alignment needs three finite roll, pitch, and yaw values.");
            foreach(var c in p.Components)if(c==null||string.IsNullOrEmpty(c.Id)||c.Override==null||c.Cad==null||c.ZeroTransform==null)throw new InvalidDataException("Incomplete component record.");
            foreach(var l in p.Links)if(l==null||string.IsNullOrEmpty(l.Id)||l.Components==null||l.ZeroFrame==null)throw new InvalidDataException("Incomplete link record.");
            foreach(var l in p.Links)if(l.Origin==null)l.Origin=new OriginDefinition();
            foreach(var j in p.Joints)if(j==null||string.IsNullOrEmpty(j.Id)||j.Axis==null)throw new InvalidDataException("Incomplete joint record.");
            return p;
        }
        public static RobotProject Load(string path) { return Parse(File.ReadAllText(path)); }
        public static void Save(string path, RobotProject project)
        {
            string full = System.IO.Path.GetFullPath(path), temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temp, Json(project), new System.Text.UTF8Encoding(false)); if (File.Exists(full)) File.Replace(temp, full, null); else File.Move(temp, full); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace CadUrdfMaker.Core
{
    public static class ComponentScope
    {
        // Retain CAD identity and captured data so refresh preserves the choice,
        // and removal can be reversed without touching the native assembly.
        public static int SetRemoved(RobotProject p,IEnumerable<string> ids,bool removed)
        {
            var selected=ids==null?new string[0]:ids.Distinct().ToArray();
            if(selected.Length==0)throw new InvalidOperationException("Select at least one unassigned component.");
            var components=new List<ComponentRecord>();
            foreach(var id in selected){
                var c=p.Components.SingleOrDefault(x=>x.Id==id);
                if(c==null)throw new InvalidOperationException("A selected component is not in this CAD snapshot. Refresh CAD and select it again.");
                if(p.Links.Any(l=>l.Components.Contains(id)))throw new InvalidOperationException(c.Name+" belongs to a link. Unassign it before removing or restoring it. No components were changed.");
                components.Add(c);
            }
            int changed=0;foreach(var c in components)if(c.Excluded!=removed){c.Excluded=removed;changed++;}
            return changed;
        }
    }
}

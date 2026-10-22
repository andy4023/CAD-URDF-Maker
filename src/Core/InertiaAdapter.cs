using System;
using System.Linq;

namespace CadUrdfMaker.Core
{
    public static class InertiaAdapter
    {
        public static double[] MassPropertyTensor(double[] matrix)
        {
            if(matrix==null||matrix.Length!=9||matrix.Any(v=>!Math3.Finite(v)))throw new ArgumentException("Native mass-property inertia requires nine finite tensor entries.");
            // IMassProperty2.GetMomentOfInertia is already a tensor, including
            // its off-diagonal signs. Do not apply the body API's conversion.
            return (double[])matrix.Clone();
        }
        public static double[] BodyTensor(double[] properties)
        {
            if(properties==null||properties.Length<12||properties.Any(value=>!Math3.Finite(value)))throw new ArgumentException("Native body mass properties require twelve finite entries.");
            // IBody2.GetMassProperties instead returns MomXY, MomZX, MomYZ
            // products. This convention is tested with a rotated analytic box.
            var v=properties;return new[]{v[6],-v[9],-v[10],-v[9],v[7],-v[11],-v[10],-v[11],v[8]};
        }
    }
}

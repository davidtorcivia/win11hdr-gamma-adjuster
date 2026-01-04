using System;
using System.Runtime.InteropServices;

namespace HDRGammaController.Interop
{
    public static class Wcs
    {
        public enum WCS_PROFILE_MANAGEMENT_SCOPE
        {
            WCS_PROFILE_MANAGEMENT_SCOPE_SYSTEM_WIDE,
            WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER
        }

        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool WcsSetDefaultColorProfile(
            WCS_PROFILE_MANAGEMENT_SCOPE scope,
            string? pDeviceName,
            int cpt, // COLORPROFILETYPE
            int cpst, // COLORPROFILESUBTYPE
            uint dwProfileID,
            string pProfileName
        );

        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool WcsGetDefaultColorProfile(
            WCS_PROFILE_MANAGEMENT_SCOPE scope,
            string? pDeviceName,
            int cpt,
            int cpst,
            uint dwProfileID,
            int cbProfileName,
            [Out] char[] pProfileName
        );
        
        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool WcsGetDefaultColorProfileSize(
            WCS_PROFILE_MANAGEMENT_SCOPE scope,
            string? pDeviceName,
            int cpt,
            int cpst,
            uint dwProfileID,
            out int pcbProfileName
        );
        
        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool InstallColorProfile(
             string? pMachineName,
             string pProfileName
        );

        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool UninstallColorProfile(
             string? pMachineName,
             string pProfileName,
             bool bDelete
        );
        
        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool AssociateColorProfileWithDevice(
            string? pMachineName,
            string pProfileFileName,
            string? pDeviceName
        );
        
        [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool DisassociateColorProfileFromDevice(
            string? pMachineName,
            string pProfileFileName,
            string? pDeviceName
        );
        
        public const int CPT_ICC = 0;
        public const int CPST_PERCEPTUAL = 0;
        public const int CPST_RELATIVE_COLORIMETRIC = 1;
        public const int CPST_SATURATION = 2;
        public const int CPST_ABSOLUTE_COLORIMETRIC = 3;
        
        // Advanced Color specific subtype for SDR-in-HDR
        // This is not officially constant, usually 0 or implicit
    }
}

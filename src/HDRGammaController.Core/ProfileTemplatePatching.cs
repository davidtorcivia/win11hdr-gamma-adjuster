using System;
using System.IO;
using System.Text;
using System.Linq;

namespace HDRGammaController.Core
{
    public static class ProfileTemplatePatching
    {
        public static void PatchProfile(string templatePath, string outputPath, double[] newLut)
        {
            byte[] data = File.ReadAllBytes(templatePath);

            // 1. Validate ICC Header
            if (data.Length < 128) throw new ArgumentException("Invalid ICC profile (too small)");

            // 2. Locate 'mhc2' tag
            int tagCount = ReadInt32BigEndian(data, 128);
            int mhc2Offset = -1;
            int mhc2Size = -1;

            for (int i = 0; i < tagCount; i++)
            {
                int entryStart = 132 + (i * 12);
                if (entryStart + 12 > data.Length) break;

                int sig = ReadInt32BigEndian(data, entryStart); // Signature
                if (sig == 0x6D686332) // 'mhc2'
                {
                    mhc2Offset = ReadInt32BigEndian(data, entryStart + 4);
                    mhc2Size = ReadInt32BigEndian(data, entryStart + 8);
                    break;
                }
            }

            if (mhc2Offset == -1) throw new ArgumentException("Template does not contain 'mhc2' tag");

            // 3. Locate LUT data within 'mhc2' tag
            // We search for a block of data that matches the size of our LUT (1024 entries).
            // Format is likely 16.16 Fixed Point (4 bytes per entry * 1024 = 4096 bytes).
            // Or just search for the specific 'lut ' structure if we knew it.
            // As a heuristic for the specific templates from the original project:
            // Look for a run of 4096 bytes.
            
            // To be robust: We assume the template is 'srgb_to_gamma2p2_100_mhc2.icm'.
            // We can search for the "Identity" LUT portion or just assume the layout based on known tools.
            // But let's look for the *offset* where we expect it to be.
            // In win11hdr templates, the mhc2 tag contains a structure.
            // We'll search for 4096 bytes of reasonable data? 
            // Wait, if we just blindly overwrite, we might kill metadata.
            
            // Let's use a heuristic: Find the sequential numbers?
            // If the template is "srgb_to_gamma2p2...", the LUT is NOT identity.
            
            // Fallback strategy:
            // The user MUST provide a template.
            // We will search for the "start of LUT" signature if it exists, or just the offset.
            // Original project: LUT starts at offset `tag_start + header_size`.
            // Structure:
            // [Header..] 
            // [LUT curve type (4)]
            // [LUT size (4)] -> 1024
            // [Data...]
            
            // Let's look for count = 1024 (0x00000400).
            int lutOffsetRel = -1;
            for (int j = 0; j < mhc2Size - 4; j += 4)
            {
                int val = ReadInt32BigEndian(data, mhc2Offset + j); // usually BE in ICC? MHC2 might be LE?
                // Windows structures are often LE. ICC is BE.
                // 'mhc2' tag is private, so it can be LE.
                // Checks both.
                if (val == 1024 || val == 0x00040000) // 1024 LE or BE
                {
                    // Verify if the following bytes look like LUT data?
                    // Let's assume this is the count.
                    // The data usually follows immediately.
                    lutOffsetRel = j + 4;
                    break;
                }
            }

            if (lutOffsetRel == -1)
            {
                // Fallback: Assume it's at a fixed offset if we fail?
                // Or throw.
                throw new InvalidOperationException("Could not locate LUT start in mhc2 tag");
            }

            int lutAbsOffset = mhc2Offset + lutOffsetRel;
            if (lutAbsOffset + 4096 > data.Length) throw new InvalidOperationException("LUT data exceeds file size");

            // 4. Overwrite LUT
            // Format: 16.16 Fixed Point (signed? usually unsigned for accumulation).
            // 1.0 = 0x00010000 (65536)
            for (int k = 0; k < 1024; k++)
            {
                double val = newLut[k];
                int fixedPt = (int)(val * 65536.0);
                WriteInt32BigEndian(data, lutAbsOffset + (k * 4), fixedPt); 
                // ICC is Big Endian. Check if MHC2 is BE.
                // Usually ICC tags are BE.
            }
            
            // 5. Zero out Profile ID (bytes 84-99 inclusive: 16 bytes)
            for (int z = 84; z < 100; z++) data[z] = 0;

            File.WriteAllBytes(outputPath, data);
        }

        private static int ReadInt32BigEndian(byte[] buf, int offset)
        {
            return (buf[offset] << 24) | (buf[offset + 1] << 16) | (buf[offset + 2] << 8) | buf[offset + 3];
        }

        private static void WriteInt32BigEndian(byte[] buf, int offset, int val)
        {
            buf[offset] = (byte)((val >> 24) & 0xFF);
            buf[offset + 1] = (byte)((val >> 16) & 0xFF);
            buf[offset + 2] = (byte)((val >> 8) & 0xFF);
            buf[offset + 3] = (byte)(val & 0xFF);
        }
    }
}

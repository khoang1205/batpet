using System;

using System.Security.Cryptography;
using System.Text;
using System.Management;

namespace LeoThap
{
    public static class HWIDHelper
    {
        public static string GetHWID()
        {
            try
            {
                // Lấy mã CPU
                string cpuInfo = string.Empty;
                using (ManagementClass mc = new ManagementClass("win32_processor"))
                {
                    using (ManagementObjectCollection moc = mc.GetInstances())
                    {
                        foreach (ManagementObject mo in moc)
                        {
                            cpuInfo = mo.Properties["processorID"].Value?.ToString() ?? "";
                            break;
                        }
                    }
                }

                // Lấy mã Ổ cứng chứa HĐH (thường là ổ C)
                string drive = "C";
                string volumeSerial = string.Empty;
                using (ManagementObject dsk = new ManagementObject(@"win32_logicaldisk.deviceid=""" + drive + @":"""))
                {
                    dsk.Get();
                    volumeSerial = dsk["VolumeSerialNumber"]?.ToString() ?? "";
                }

                // Kết hợp lại và băm MD5 cho chuỗi đẹp và thống nhất
                string rawHwid = cpuInfo + volumeSerial;
                using (MD5 md5 = MD5.Create())
                {
                    byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(rawHwid));
                    return BitConverter.ToString(hash).Replace("-", ""); // Trả về dạng: A1B2C3D4...
                }
            }
            catch
            {
                // Fallback nếu lỗi quyền
                return "UNKNOWN-HWID-" + Environment.MachineName;
            }
        }
    }
}
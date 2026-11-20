using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BSCustomClevoController.Utility
{
	// Token: 0x02000006 RID: 6
	public class InsydeDriver
	{
		// Token: 0x0600001C RID: 28
		[DllImport("InsydeDCHU.dll", EntryPoint = "GetDCHU_Data_Integer")]
		[DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
		private static extern int NativeGetDCHU_Data_Integer(int command, ref int data);

		// Token: 0x0600001D RID: 29
		[DllImport("InsydeDCHU.dll", EntryPoint = "SetDCHU_Data")]
		[DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
		private static extern int NativeSetDCHU_Data(int command, byte[] buffer, int length);

		// Token: 0x0600001E RID: 30
		[DllImport("InsydeDCHU.dll", EntryPoint = "SetDCHU_DataEx")]
		[DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
		private static extern int NativeSetDCHU_DataEx(int command, byte[] buffer, int length, ref byte OutputBuffer);

		// Token: 0x0600001F RID: 31
		[DllImport("InsydeDCHU.dll", EntryPoint = "GetDCHU_Data_Buffer")]
		[DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
		private static extern int NativeGetDCHU_Data_Buffer(int command, ref byte buffer);

		// Token: 0x06000020 RID: 32
		[DllImport("InsydeDCHU.dll", EntryPoint = "ReadAppSettings")]
		[DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
		private static extern int NativeReadAppSettings(int page, int offset, int length, ref byte buffer);

		// Token: 0x06000021 RID: 33
		[DllImport("InsydeDCHU.dll", EntryPoint = "WriteAppSettings")]
		[DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
		private static extern int NativeWriteAppSettings(int page, int offset, int length, ref byte buffer);

		// InsydeDCHU.dll shares one ACPI buffer; concurrent calls from the EC poll thread and UI timers corrupt each other's reads.
		private static readonly object DchuLock = new object();

		public static int GetDCHU_Data_Integer(int command, ref int data)
		{
			lock (DchuLock) { return NativeGetDCHU_Data_Integer(command, ref data); }
		}

		public static int SetDCHU_Data(int command, byte[] buffer, int length)
		{
			if (buffer == null) throw new ArgumentNullException(nameof(buffer));
			if (length < 0 || length > buffer.Length) throw new ArgumentOutOfRangeException(nameof(length));
			lock (DchuLock) { return NativeSetDCHU_Data(command, buffer, length); }
		}

		public static int SetDCHU_DataEx(int command, byte[] buffer, int length, ref byte OutputBuffer)
		{
			if (buffer == null) throw new ArgumentNullException(nameof(buffer));
			if (length < 0 || length > buffer.Length) throw new ArgumentOutOfRangeException(nameof(length));
			lock (DchuLock) { return NativeSetDCHU_DataEx(command, buffer, length, ref OutputBuffer); }
		}

		public static int GetDCHU_Data_Buffer(int command, ref byte buffer)
		{
			lock (DchuLock) { return NativeGetDCHU_Data_Buffer(command, ref buffer); }
		}

		public static int ReadAppSettings(int page, int offset, int length, ref byte buffer)
		{
			if (page < 0) throw new ArgumentOutOfRangeException(nameof(page));
			if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
			if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
			lock (DchuLock) { return NativeReadAppSettings(page, offset, length, ref buffer); }
		}

		public static int WriteAppSettings(int page, int offset, int length, ref byte buffer)
		{
			if (page < 0) throw new ArgumentOutOfRangeException(nameof(page));
			if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
			if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
			lock (DchuLock) { return NativeWriteAppSettings(page, offset, length, ref buffer); }
		}

		// Token: 0x06000022 RID: 34 RVA: 0x00004AF4 File Offset: 0x00002CF4
		public int SetWMI(int command, uint data)
		{
			byte[] buffer = BitConverter.GetBytes(data);
			return InsydeDriver.SetDCHU_Data(command, buffer, 4);
		}

		// Token: 0x06000023 RID: 35 RVA: 0x00004B44 File Offset: 0x00002D44
		public int SetWMI(int command, int SubCommand, uint data)
		{
			byte[] array = BitConverter.GetBytes(data);
			array[3] = Convert.ToByte(SubCommand);
			return InsydeDriver.SetDCHU_Data(command, array, 4);
		}

		// Token: 0x06000024 RID: 36 RVA: 0x00004BC0 File Offset: 0x00002DC0
		public int SetWMIPackage(int command, byte[] buffer)
		{
			if (buffer == null || buffer.Length < 256)
			{
				throw new ArgumentException("WMI package buffer must be at least 256 bytes.", nameof(buffer));
			}
			return InsydeDriver.SetDCHU_Data(command, buffer, 256);
		}

		// Token: 0x06000025 RID: 37 RVA: 0x00004BF8 File Offset: 0x00002DF8
		public int SetWMIPackageEx(int command, byte[] buffer, ref byte[] o_buffer)
		{
			if (buffer == null || buffer.Length < 256 || o_buffer == null || o_buffer.Length == 0)
			{
				return 0;
			}
			try
			{
				return InsydeDriver.SetDCHU_DataEx(command, buffer, 256, ref o_buffer[0]);
			}
			catch (Exception ex)
			{
				AppLogger.Log("InsydeDriver.SetWMIPackageEx", ex);
				return 0;
			}
		}

		// Token: 0x06000026 RID: 38 RVA: 0x00004C48 File Offset: 0x00002E48
		public int GetWMI(int command)
		{
			int result = 0;
			InsydeDriver.GetDCHU_Data_Integer(command, ref result);
			return result;
		}

		// Token: 0x06000027 RID: 39 RVA: 0x00004C8C File Offset: 0x00002E8C
		public byte[] GetWMIPackage(int command)
		{
			byte[] array = new byte[256];
			InsydeDriver.GetDCHU_Data_Buffer(command, ref array[0]);
			return array;
		}

		// Token: 0x06000028 RID: 40 RVA: 0x00004CB8 File Offset: 0x00002EB8
		public byte[] GetAPPData(int page, int Index, int length)
		{
			if (length <= 0)
			{
				return Array.Empty<byte>();
			}
			byte[] array = new byte[length];
			InsydeDriver.ReadAppSettings(page, Index, length, ref array[0]);
			return array;
		}

		// Token: 0x06000029 RID: 41 RVA: 0x00004CE4 File Offset: 0x00002EE4
		public int SetAPPData(int page, int Index, int length, byte[] Data)
		{
			if (Data == null || length <= 0 || length > Data.Length)
			{
				return 0;
			}
			return InsydeDriver.WriteAppSettings(page, Index, length, ref Data[0]);
		}

		// Token: 0x0600002A RID: 42 RVA: 0x00004D08 File Offset: 0x00002F08
		public void Init_WMI()
		{
			int wmi = this.GetWMI(70);
			byte[] array = Global.insyde.GetAPPData(7, 0, 256);
			if (array == null || array.Length < 2)
			{
				return;
			}
			int num = ((int)array[0] << 8) + (int)array[1];
			int num2 = num;
			int num3 = num2;
			if (num3 != 0)
			{
				if (num3 != 256)
				{
					this.BIOSSpecialFeature_v0_0(array);
				}
				else
				{
					this.BIOSSpecialFeature_v1_0(array);
				}
			}
			else
			{
				this.BIOSSpecialFeature_v0_0(array);
			}
		}

		// Token: 0x0600002B RID: 43 RVA: 0x00004D7C File Offset: 0x00002F7C
		private void BIOSSpecialFeature_v0_0(byte[] offset)
		{
			int wmi = this.GetWMI(16);
			bool flag = (wmi & 1) == 1;
			if (flag)
			{
				Global.support_bit.UWP_PowerMode = true;
			}
			bool flag2 = (wmi & 2) == 2;
			if (flag2)
			{
				Global.support_bit.UWP_Flexikey = true;
			}
			bool flag3 = (wmi & 4) == 4;
			if (flag3)
			{
				Global.support_bit.UWP_FlexiAccess = true;
			}
			bool flag4 = (wmi & 8) == 8;
			if (flag4)
			{
				Global.support_bit.UWP_PerkeyKB = true;
			}
			bool flag5 = (wmi & 16) == 16;
			if (flag5)
			{
				Global.support_bit.UWP_RGBKB = true;
			}
			bool flag6 = (wmi & 32) == 32;
			if (flag6)
			{
				Global.support_bit.UWP_GPUOC = true;
			}
			bool flag7 = (wmi & 64) == 64;
			if (flag7)
			{
				Global.support_bit.UWP_CPUOC = true;
			}
			bool flag8 = (wmi & 128) == 128;
			if (flag8)
			{
				Global.support_bit.UWP_FanSetting = true;
			}
			int wmi2 = this.GetWMI(122);
			bool flag9 = ((wmi2 >> 15) & 1) == 1;
			if (flag9)
			{
				Global.support_bit.FanLess = true;
			}
			int wmi3 = this.GetWMI(96);
			bool flag10 = ((wmi3 >> 7) & 1) == 1;
			if (flag10)
			{
				Global.support_bit.AntiDust_Fan = true;
			}
			bool flag11 = ((wmi3 >> 10) & 1) == 1;
			if (flag11)
			{
				Global.support_bit.FanOffset = false;
			}
		}

		// Token: 0x0600002C RID: 44 RVA: 0x00004EB8 File Offset: 0x000030B8
		private void BIOSSpecialFeature_v1_0(byte[] offset)
		{
			int num = ((int)offset[5] << 24) + ((int)offset[4] << 16) + ((int)offset[3] << 8) + (int)offset[2];
			int num2 = ((int)offset[9] << 24) + ((int)offset[8] << 16) + ((int)offset[7] << 8) + (int)offset[6];
			int num3 = ((int)offset[11] << 8) + (int)offset[10];
			int num4 = ((int)offset[14] << 8) + (int)offset[13];
			bool flag = (num4 & 1) == 1;
			if (flag)
			{
				Global.support_bit.UWP_PowerMode = true;
			}
			bool flag2 = (num4 & 2) == 2;
			if (flag2)
			{
				Global.support_bit.UWP_Flexikey = true;
			}
			bool flag3 = (num4 & 4) == 4;
			if (flag3)
			{
				Global.support_bit.UWP_FlexiAccess = true;
			}
			bool flag4 = (num4 & 8) == 8;
			if (flag4)
			{
				Global.support_bit.UWP_PerkeyKB = true;
			}
			bool flag5 = (num4 & 16) == 16;
			if (flag5)
			{
				Global.support_bit.UWP_RGBKB = true;
			}
			bool flag6 = (num4 & 32) == 32;
			if (flag6)
			{
				Global.support_bit.UWP_GPUOC = true;
			}
			bool flag7 = (num4 & 64) == 64;
			if (flag7)
			{
				Global.support_bit.UWP_CPUOC = true;
			}
			bool flag8 = (num4 & 128) == 128;
			if (flag8)
			{
				Global.support_bit.UWP_FanSetting = true;
			}
			bool flag9 = ((num2 >> 15) & 1) == 1;
			if (flag9)
			{
				Global.support_bit.FanLess = true;
			}
			bool flag10 = ((num3 >> 7) & 1) == 1;
			if (flag10)
			{
				Global.support_bit.AntiDust_Fan = true;
			}
			bool flag11 = ((num3 >> 10) & 1) == 1;
			if (flag11)
			{
				Global.support_bit.FanOffset = false;
			}
			bool flag12 = ((num3 >> 12) & 1) == 1;
			if (flag12)
			{
				Global.support_bit.DTT = true;
			}
			bool flag13 = ((offset[16] >> 4) & 1) == 1;
			if (flag13)
			{
				Global.support_bit.TurboFan = true;
			}
		}

		// Token: 0x0600002D RID: 45 RVA: 0x00005058 File Offset: 0x00003258
		public static string GetECVersion()
		{
			byte[] array = new byte[256];
			byte[] array2 = new byte[256];
			byte[] array3 = new byte[24];
			string text = "1.";
			lock (DchuLock)
			{
				array[0] = 1;
				array[1] = 0;
				array[6] = 222;
				NativeSetDCHU_DataEx(4, array, 256, ref array2[0]);

				array[1] = 1;
				NativeSetDCHU_DataEx(4, array, 256, ref array2[0]);
				Array.Copy(array2, 1, array3, 0, 5);

				array[1] = 2;
				NativeSetDCHU_DataEx(4, array, 256, ref array2[0]);
				Array.Copy(array2, 1, array3, 5, 5);

				array[1] = 3;
				NativeSetDCHU_DataEx(4, array, 256, ref array2[0]);
				Array.Copy(array2, 1, array3, 10, 5);
			}
			text += Encoding.ASCII.GetString(array3);
			string[] array4 = text.Split(new char[] { '$' });
			return array4[0].Trim(new char[1]);
		}
	}
}

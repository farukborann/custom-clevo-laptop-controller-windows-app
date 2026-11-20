namespace BSCustomClevoController.Utility
{
	public static class Global
	{
		public static readonly RWReg RW_REG = new RWReg();

		public static readonly Support_BIT support_bit = new Support_BIT();

		public static readonly InsydeDriver insyde = new InsydeDriver();

		public static readonly byte[] kled_kb_array = new byte[255];

		public static int Flexikey_LED_Language { get; set; }
	}
}

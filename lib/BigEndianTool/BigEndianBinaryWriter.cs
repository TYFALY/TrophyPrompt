using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

public class BigEndianBinaryWriter : BinaryWriter {
    private byte[] a16 = new byte[2];
    private byte[] a32 = new byte[4];
    private byte[] a64 = new byte[8];
    public BigEndianBinaryWriter(System.IO.Stream stream) : base(stream) { }
    public override void Write(short s) {
        a16 = BitConverter.GetBytes(s);
        Array.Reverse(a16);
        base.Write(a16);
    }
    public override void Write(ushort s) {
        a16 = BitConverter.GetBytes(s);
        Array.Reverse(a16);
        base.Write(a16);
    }
    public override void Write(int s) {
        a32 = BitConverter.GetBytes(s);
        Array.Reverse(a32);
        base.Write(a32);
    }
    public override void Write(uint s) {
        a32 = BitConverter.GetBytes(s);
        Array.Reverse(a32);
        base.Write(a32);
    }
    public override void Write(long s) {
        a64 = BitConverter.GetBytes(s);
        Array.Reverse(a64);
        base.Write(a64);
    }
    public override void Write(ulong s) {
        a64 = BitConverter.GetBytes(s);
        Array.Reverse(a64);
        base.Write(a64);
    }
    public void Write(DateTime datetime) {
        Write(datetime.Ticks/10);
    }
}

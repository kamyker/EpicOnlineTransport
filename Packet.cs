using System;
using System.Buffers;

namespace EpicTransport
{
public struct Packet : IDisposable
{
	public const int headerSize = sizeof(uint) + sizeof(uint) + 1;

	public Packet(byte[] array, int arrayLength) : this()
	{
		id = BitConverter.ToInt32(array, 0);
		fragment = BitConverter.ToInt32(array, 4);
		moreFragments = array[8] == 1;

		data = new PooledArray<byte>(arrayLength - 9);
		// data = new byte[outBytesWritten - 9];
		Array.Copy(array, 9, data.Array, 0, data.Length);
	}

	public Packet(ArraySegment<byte> array) : this()
	{
		id = 0;
		fragment = 0;
		moreFragments = false;

		data = new PooledArray<byte>(array.Count);

		Array.Copy(array.Array, array.Offset, data.Array, 0, data.Length);
	}

	public Packet(ArraySegment<byte> array, int id, int fragment,
	              bool moreFragments) : this()
	{
		this.id = id;
		this.fragment = fragment;
		this.moreFragments = moreFragments;

		data = new PooledArray<byte>(array.Count);

		Array.Copy(array.Array, array.Offset, data.Array, 0, data.Length);
	}

	public int size => headerSize + data.Length;

	// header
	public int id;
	public int fragment;
	public bool moreFragments;

	// body
	public PooledArray<byte> data {get; private set;}

	public byte[] ToBytes()
	{
		// ArrayPool<byte>.Shared.Rent(size);
		byte[] array = new byte[size];

		// Copy id
		array[0] = (byte)id;
		array[1] = (byte)(id >> 8);
		array[2] = (byte)(id >> 0x10);
		array[3] = (byte)(id >> 0x18);

		// Copy fragment
		array[4] = (byte)fragment;
		array[5] = (byte)(fragment >> 8);
		array[6] = (byte)(fragment >> 0x10);
		array[7] = (byte)(fragment >> 0x18);

		array[8] = moreFragments ? (byte)1 : (byte)0;

		Array.Copy(data.Array, 0, array, 9, data.Length);

		return array;
	}

	public void Dispose()
	{
		data.Dispose();
	}
}
}
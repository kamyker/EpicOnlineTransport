using System;
using System.Buffers;
using UnityEngine;

namespace EpicTransport
{
public struct PooledArray<T> : IDisposable
{
	public T[] Array;

	public int Length;
	public PooledArray(int minLength) : this()
	{
		Length = minLength;
		Debug.Log("minLength = " + minLength);
		Array = ArrayPool<T>.Shared.Rent(minLength);
	}

	public void Dispose()
	{
		ArrayPool<T>.Shared.Return(Array);
	}
}
}
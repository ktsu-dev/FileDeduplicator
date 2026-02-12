// Copyright (c) ktsu.dev
// All rights reserved.
// Licensed under the MIT license.

namespace ktsu.FileDeduplicator;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

using ktsu.Semantics.Paths;

internal static class FileHasher
{
	private static readonly Lock ConsoleLock = new();

	internal static Dictionary<AbsoluteFilePath, string> HashFiles(IReadOnlyList<AbsoluteFilePath> filePaths)
	{
		ConcurrentDictionary<AbsoluteFilePath, string> results = new();

		Parallel.ForEach(filePaths, filePath =>
		{
			try
			{
				string hash = ComputeHash(filePath);
				results[filePath] = hash;

				lock (ConsoleLock)
				{
					Console.WriteLine($"  Hashed: {filePath.FileName} -> {hash[..12]}...");
				}
			}
			catch (IOException ex)
			{
				lock (ConsoleLock)
				{
					Console.WriteLine($"  Error hashing {filePath.FileName}: {ex.Message}");
				}
			}
		});

		return new Dictionary<AbsoluteFilePath, string>(results);
	}

	internal static string ComputeHash(AbsoluteFilePath filePath)
	{
		using FileStream stream = File.OpenRead(filePath.WeakString);
		byte[] hashBytes = SHA256.HashData(stream);
		return Convert.ToHexStringLower(hashBytes);
	}
}

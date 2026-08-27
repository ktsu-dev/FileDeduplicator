// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator.Test;

using System.Runtime.CompilerServices;

using ktsu.Semantics.Paths;
using ktsu.Semantics.Strings;

/// <summary>
/// A throwaway directory tree under the system temp path, deleted on disposal.
/// </summary>
/// <remarks>
/// These tests drive the real filesystem rather than an abstraction, because that is what the
/// production code uses -- <see cref="FileScanner"/> calls <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/>
/// and <see cref="Deduplicator"/> calls <see cref="File.Delete"/> directly. Faking those would
/// test a seam that does not exist and would not catch a mistake in the delete path, which is the
/// part of this tool that destroys data.
/// </remarks>
internal sealed class TempTree : IDisposable
{
	/// <summary>
	/// Gets the root of the throwaway tree.
	/// </summary>
	internal AbsoluteDirectoryPath Root { get; }

	/// <summary>
	/// Creates a tree in a directory named after the calling test, so a leaked directory names
	/// the test that leaked it.
	/// </summary>
	/// <param name="caller">Supplied by the compiler; do not pass explicitly.</param>
	internal TempTree([CallerMemberName] string caller = "")
	{
		string path = Path.Combine(Path.GetTempPath(), $"ktsu-dedupe-{caller}-{Guid.NewGuid():N}");
		_ = Directory.CreateDirectory(path);
		Root = path.As<AbsoluteDirectoryPath>();
	}

	/// <summary>
	/// Writes a file with the given content, creating any intermediate directories.
	/// </summary>
	/// <param name="relativePath">Path relative to <see cref="Root"/>, using forward slashes.</param>
	/// <param name="content">The content to write.</param>
	/// <returns>The absolute path of the written file.</returns>
	internal AbsoluteFilePath Write(string relativePath, string content)
	{
		string full = Path.Combine(Root.WeakString, relativePath.Replace('/', Path.DirectorySeparatorChar));
		_ = Directory.CreateDirectory(Path.GetDirectoryName(full)!);
		File.WriteAllText(full, content);
		return full.As<AbsoluteFilePath>();
	}

	/// <summary>
	/// Gets whether a file written through <see cref="Write"/> still exists.
	/// </summary>
	/// <param name="path">The file to check.</param>
	/// <returns><see langword="true"/> if it is still on disk.</returns>
	internal static bool Exists(AbsoluteFilePath path) => File.Exists(path.WeakString);

	/// <inheritdoc />
	public void Dispose()
	{
		try
		{
			Directory.Delete(Root.WeakString, recursive: true);
		}
		catch (DirectoryNotFoundException)
		{
			// Already gone; nothing to clean up.
		}
		catch (IOException)
		{
			// A leaked temp directory is not worth failing an otherwise passing test over.
		}
	}
}

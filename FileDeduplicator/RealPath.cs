// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.FileDeduplicator;

using System.IO;

/// <summary>
/// Resolves a path to the location it leads to now, following every symbolic link and junction
/// along it.
/// </summary>
/// <remarks>
/// The scan records path strings, and a path string names whatever its directories lead to at the
/// moment it is used. A directory under the root that has since been replaced by a link sends a
/// recorded path somewhere else entirely, so before deleting through one the deletion path asks
/// where it actually leads.
/// </remarks>
internal static class RealPath
{
	/// <summary>
	/// The most links followed while resolving one path, the same bound Linux puts on a lookup.
	/// </summary>
	private const int MaxLinks = 40;

	private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

	/// <summary>
	/// Gets the comparison that matches how this platform's default file system compares names.
	/// </summary>
	internal static StringComparison Comparison { get; } =
		OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

	/// <summary>
	/// Resolves an absolute path, following every link along it.
	/// </summary>
	/// <param name="path">An absolute path.</param>
	/// <param name="realPath">The path with no links left in it, when it could be resolved.</param>
	/// <returns>
	/// <see langword="false"/> when a link could not be read, the links nest too deeply, or a
	/// component could not be inspected.
	/// </returns>
	internal static bool TryResolve(string path, out string realPath)
	{
		int linksFollowed = 0;
		string? resolved = Resolve(path, ref linksFollowed);
		realPath = resolved ?? string.Empty;
		return resolved is not null;
	}

	/// <summary>
	/// Reports whether <paramref name="path"/> is <paramref name="root"/> or lies beneath it, with
	/// both already resolved by <see cref="TryResolve"/>.
	/// </summary>
	/// <param name="path">The resolved path to check.</param>
	/// <param name="root">The resolved root.</param>
	/// <returns><see langword="true"/> if <paramref name="path"/> is inside <paramref name="root"/>.</returns>
	internal static bool IsWithin(string path, string root)
	{
		string trimmedRoot = Path.TrimEndingDirectorySeparator(root);
		string trimmedPath = Path.TrimEndingDirectorySeparator(path);

		return string.Equals(trimmedPath, trimmedRoot, Comparison)
			|| trimmedPath.StartsWith(trimmedRoot + Path.DirectorySeparatorChar, Comparison)
			|| (trimmedRoot.EndsWith(Path.DirectorySeparatorChar) && trimmedPath.StartsWith(trimmedRoot, Comparison));
	}

	private static string? Resolve(string path, ref int linksFollowed)
	{
		if (!Path.IsPathFullyQualified(path))
		{
			return null;
		}

		string current = Path.GetPathRoot(path)!;

		foreach (string part in path[current.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries))
		{
			if (part == ".")
			{
				continue;
			}

			// current never holds a link, so going up from it lexically is going up for real.
			if (part == "..")
			{
				current = Path.GetDirectoryName(current) ?? current;
				continue;
			}

			string next = Path.Combine(current, part);
			string? target;

			try
			{
				target = new FileInfo(next).LinkTarget;
			}
			catch (IOException)
			{
				return null;
			}
			catch (UnauthorizedAccessException)
			{
				return null;
			}

			if (target is null)
			{
				current = next;
				continue;
			}

			if (++linksFollowed > MaxLinks)
			{
				return null;
			}

			// A relative target is relative to the directory holding the link, which is current.
			string? resolvedTarget = Resolve(Path.IsPathFullyQualified(target) ? target : Path.Combine(current, target), ref linksFollowed);
			if (resolvedTarget is null)
			{
				return null;
			}

			current = resolvedTarget;
		}

		return current;
	}
}

using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;

namespace Sdl3Sharp.SourceGeneration;

public sealed class TypeSymbolDisplayStringComparer(SymbolDisplayFormat? format = null, StringComparison? comparison = null) : IComparer<ITypeSymbol?>
{
	public static readonly TypeSymbolDisplayStringComparer Default = new();

	public int Compare(ITypeSymbol? x, ITypeSymbol? y)
	{
		if (ReferenceEquals(x, y) || SymbolEqualityComparer.Default.Equals(x, y))
		{
			return 0;
		}
		
		if (x is null)
		{
			return -1;
		}

		if (y is null)
		{
			return 1;
		}

		return comparison switch
		{
			StringComparison comparisonType => string.Compare(x.ToDisplayString(format), y.ToDisplayString(format), comparisonType),
			_ => string.Compare(x.ToDisplayString(format), y.ToDisplayString(format)),
		};
	}
}

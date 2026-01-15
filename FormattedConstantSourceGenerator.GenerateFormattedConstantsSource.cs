using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Sdl3Sharp.SourceGeneration;

partial class FormattedConstantSourceGenerator
{
	private const string GeneratedConstantsOutputFileName = $"{nameof(Sdl3Sharp)}.{nameof(SourceGeneration)}.FormattedConstants.g.cs";

	private static readonly DiagnosticDescriptor mCouldNotFindRequiredTypeReadOnlySpan = new(
		id: $"{DiagnosticDescriptorIdPrefix}0001",
		title: "Could not find required type 'System.ReadOnlySpan<T>'",
		messageFormat: "The required type 'System.ReadOnlySpan<T>' could not be found",
		category: DiagnosticDescriptorCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	private static readonly DiagnosticDescriptor mNullFormatStringDescriptor = new(
		id: $"{DiagnosticDescriptorIdPrefix}0002",
		title: "Null format string",
		messageFormat: "The format string cannot be null",
		category: DiagnosticDescriptorCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	private static readonly DiagnosticDescriptor mFormatExceptionDescriptor = new(
		id: $"{DiagnosticDescriptorIdPrefix}0003",
		title: "Format string error",
		messageFormat: "Error formatting string: {0}",
		category: DiagnosticDescriptorCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	private static void GenerateFormattedConstantsSource(
		SourceProductionContext spc,
		(Compilation compilation, ImmutableArray<FormattedConstantData> formattedConstants) data)
	{
		var (compilation, formattedConstants) = data;

		if (formattedConstants.IsDefaultOrEmpty)
		{
			return;
		}

		var readOnlySpanType = compilation.GetTypeByMetadataName("System.ReadOnlySpan`1");
		if (readOnlySpanType is null)
		{
			spc.ReportDiagnostic(Diagnostic.Create(mCouldNotFindRequiredTypeReadOnlySpan,
				location: Location.None
			));
			return;
		}

		var buildTree = new BuildTree();

		foreach (var constantData in formattedConstants)
		{
			if (!constantData.Validate(spc, readOnlySpanType, out var returnKind))
			{
				continue;
			}

			if (constantData.Format is null)
			{
				spc.ReportDiagnostic(Diagnostic.Create(mNullFormatStringDescriptor,
					location: constantData.Location
				));
				continue;
			}

			string formattedValue;
			try
			{
				formattedValue = GetFormatProvider(constantData.Culture) switch
				{
					null => string.Format(constantData.Format, [..constantData.Args]),
					var formatProvider => string.Format(formatProvider, constantData.Format, [..constantData.Args])
				};
			}
			catch (FormatException ex)
			{
				spc.ReportDiagnostic(Diagnostic.Create(mFormatExceptionDescriptor,
					location: constantData.Location,
					ex.Message
				));
				continue;
			}

			buildTree.TryAddMemberOutput(new(constantData, GenerateResultLiteral(formattedValue, returnKind), returnKind), spc);
		}

		buildTree.Consolidate();

		if (buildTree.IsEmpty)
		{
			return;
		}

		var builder = new StringBuilder();

		builder.Append("""
			#nullable enable

			""");

		buildTree.Print(builder);

		builder.Append("""

			#nullable restore
			""");

		spc.AddSource(GeneratedConstantsOutputFileName, SourceText.From(text: builder.ToString(), encoding: Encoding.UTF8));
	}

	private static IFormatProvider? GetFormatProvider(FormatCulture culture) => culture switch
	{
		FormatCulture.Invariant => CultureInfo.InvariantCulture,
		/*
		FormatCulture.CurrentCulture => CultureInfo.CurrentCulture,
		FormatCulture.CurrentUICulture => CultureInfo.CurrentUICulture,
		*/
		_ => null
	};

	private static string GenerateResultLiteral(string value, ReturnTypeKind returnKind)
	{
		if (value.Length is 0)
		{
			return returnKind switch
			{
				ReturnTypeKind.String or ReturnTypeKind.NullableString => "string.Empty",
				_ => "[]"
			};
		}

		var escapedValue = SymbolDisplay.FormatLiteral(value, quote: false);

		return returnKind switch
		{
			ReturnTypeKind.String or ReturnTypeKind.NullableString => $"\"{escapedValue}\"",
			ReturnTypeKind.ReadOnlySpanChar => $"\"{escapedValue}\"",
			ReturnTypeKind.ReadOnlySpanByte => $"\"{escapedValue}\"u8",
			_ => $"\"{escapedValue}\""
		};
	}
}
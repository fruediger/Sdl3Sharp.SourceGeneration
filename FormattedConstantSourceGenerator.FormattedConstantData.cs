using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace Sdl3Sharp.SourceGeneration;

partial class FormattedConstantSourceGenerator
{
	private static readonly DiagnosticDescriptor mUnsupportedMemberDeclarationSignatureDescriptor = new(
		id: $"{DiagnosticDescriptorIdPrefix}0031",
		title: "Unsupported target declaration signature",
		messageFormat: "The target declaration of \"{0}\" must be a 'static' 'partial' method or a 'static' 'partial' property with only a 'get' accessor, returning 'string', 'string?', 'ReadOnlySpan<char>', or 'ReadOnlySpan<byte>'",
		category: DiagnosticDescriptorCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	private enum FormatCulture
	{
		Default = default,
		Invariant,
		/*
		CurrentCulture,
		CurrentUICulture
		*/
	}

	private enum ReturnTypeKind
	{
		String,
		NullableString,
		ReadOnlySpanChar,
		ReadOnlySpanByte
	}

	/// <summary>Represents a formatted constant member (method or property) declaration</summary>
	private sealed class FormattedConstantData
	{
		public string? Format { get; }

		public ImmutableArray<object?> Args { get; }

		public FormatCulture Culture { get; }

		public ISymbol TargetSymbol { get; }

		public SyntaxTokenList TargetModifiers { get; }

		public Location? Location { get; }

		public bool IsProperty { get; }

		public ReturnTypeKind ReturnKind { get; }

		private FormattedConstantData(string? format, ImmutableArray<object?> args, FormatCulture culture, ISymbol targetSymbol, SyntaxTokenList targetModifiers, Location? location, bool isProperty, ReturnTypeKind returnKind)
		{
			Format = format;
			Args = args;
			Culture = culture;
			TargetSymbol = targetSymbol;
			TargetModifiers = targetModifiers;
			Location = location;
			IsProperty = isProperty;
			ReturnKind = returnKind;
		}

		public static FormattedConstantData? Create(GeneratorAttributeSyntaxContext gasc, CancellationToken cancellationToken)
		{
			if (gasc.Attributes is not [{ ApplicationSyntaxReference: var syntaxRef, ConstructorArguments: var ctorArgs, NamedArguments: var namedArgs }, ..])
			{
				return null;
			}

			string? format = ctorArgs switch
			{
				[{ Value: string f }, ..] => f,
				[{ IsNull: true }, ..] => null,
				_ => null
			};

			var args = ctorArgs switch
			{
				[_, { Kind: TypedConstantKind.Array, Values: var values }] => [..values.Select(static v => v.Value)],
				_ => ImmutableArray<object?>.Empty
			};

			var culture = namedArgs.FirstOrDefault(static p => p.Key is FormattedConstantAttributeCulturePropertyName).Value switch
			{
				{ IsNull: false, Kind: TypedConstantKind.Enum, Value: int intValue } => unchecked((FormatCulture)intValue),
				_ => FormatCulture.Default
			};

			var location = syntaxRef?.GetSyntax(cancellationToken).GetLocation();

			if (gasc is { TargetSymbol: IMethodSymbol targetMethod, TargetNode: MethodDeclarationSyntax { Modifiers: var methodModifiers } })
			{
				return new FormattedConstantData(format, args, culture, targetMethod, methodModifiers, location, isProperty: false, ReturnTypeKind.String);
			}

			if (gasc is { TargetSymbol: IPropertySymbol targetProperty, TargetNode: PropertyDeclarationSyntax { Modifiers: var propertyModifiers } })
			{
				return new FormattedConstantData(format, args, culture, targetProperty, propertyModifiers, location, isProperty: true, ReturnTypeKind.String);
			}

			return null;
		}

		public bool Validate(SourceProductionContext spc, INamedTypeSymbol readOnlySpanType, out ReturnTypeKind validatedReturnKind)
		{
			validatedReturnKind = ReturnTypeKind.String;

			ITypeSymbol returnType;
			switch (TargetSymbol)
			{
				case IMethodSymbol { IsStatic: true, IsPartialDefinition: true, ReturnType: var methodReturnType }:
					returnType = methodReturnType;
					break;

				case IPropertySymbol { IsStatic: true, GetMethod: not null, SetMethod: null, Type: var propertyType } when HasPartialModifier():
					returnType = propertyType;
					break;

				default:
					spc.ReportDiagnostic(Diagnostic.Create(mUnsupportedMemberDeclarationSignatureDescriptor,
						location: Location,
						TargetSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
					));
					return false;
			}

			if (!TryGetReturnTypeKind(returnType, readOnlySpanType, out validatedReturnKind))
			{
				spc.ReportDiagnostic(Diagnostic.Create(mUnsupportedMemberDeclarationSignatureDescriptor,
					location: Location,
					TargetSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
				));
				return false;
			}

			return true;
		}

		private bool HasPartialModifier()
			=> TargetModifiers.Any(SyntaxKind.PartialKeyword);

		private static bool TryGetReturnTypeKind(ITypeSymbol type, INamedTypeSymbol readOnlySpanType, out ReturnTypeKind kind)
		{
			switch (type)
			{
				case { SpecialType: SpecialType.System_String } or { OriginalDefinition.SpecialType: SpecialType.System_Char }:
					kind = type.NullableAnnotation is NullableAnnotation.Annotated
						? ReturnTypeKind.NullableString
						: ReturnTypeKind.String;
					return true;

				case INamedTypeSymbol { TypeArguments: [var typeArg], OriginalDefinition: var originalDefinition } when SymbolEqualityComparer.Default.Equals(originalDefinition, readOnlySpanType):
					switch (typeArg.SpecialType)
					{
						case SpecialType.System_Char:
							kind = ReturnTypeKind.ReadOnlySpanChar;
							return true;

						case SpecialType.System_Byte:
							kind = ReturnTypeKind.ReadOnlySpanByte;
							return true;
					}
					break;
			}

			kind = default;
			return false;
		}
	}
}
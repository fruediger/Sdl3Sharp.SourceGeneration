using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace Sdl3Sharp.SourceGeneration;

partial class FormattedConstantSourceGenerator
{
	private static readonly DiagnosticDescriptor mUnsupportedTypeDeclarationSignatureDescriptor = new(
		id: $"{DiagnosticDescriptorIdPrefix}0011",
		title: "Unsupported type declaration signature",
		messageFormat: "The type declaration of \"{0}\" must be a 'partial' type in order for it to contain formatted constant members or contain nested types containing formatted constant members",
		category: DiagnosticDescriptorCategory,
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true
	);

	private const string IndentationPrintPrefix = "\t";

	private readonly struct BuildTree()
	{
		private readonly struct NamespaceNode()
		{
			public readonly Dictionary<INamespaceSymbol, NamespaceNode> NestedNamespaces = new(SymbolEqualityComparer.Default);
			public readonly Dictionary<INamedTypeSymbol, TypeNode> ContainedTypes = new(SymbolEqualityComparer.Default);

			public readonly bool IsEmpty => NestedNamespaces.Count is not > 0 && ContainedTypes.Count is not > 0;

			public readonly bool Consolidate(Stack<INamespaceOrTypeSymbol> symbolBuffer)
			{
				var initialBufferSize = symbolBuffer.Count;

				foreach (var (@namespace, namespaceNode) in NestedNamespaces)
				{
					if (namespaceNode.Consolidate(symbolBuffer))
					{
						symbolBuffer.Push(@namespace);
					}
				}

				while (initialBufferSize < symbolBuffer.Count)
				{
					NestedNamespaces.Remove((INamespaceSymbol)symbolBuffer.Pop());
				}

				foreach (var (type, typeNode) in ContainedTypes)
				{
					if (typeNode.Consolidate(symbolBuffer))
					{
						symbolBuffer.Push(type);
					}
				}

				while (initialBufferSize < symbolBuffer.Count)
				{
					ContainedTypes.Remove((INamedTypeSymbol)symbolBuffer.Pop());
				}

				return IsEmpty;
			}

			public readonly void Print(INamespaceSymbol @namespace, StringBuilder builder, string indentation)
			{
				var namespaceName = @namespace.Name;
				var namespaceNode = this;

				for (;
					namespaceNode is { NestedNamespaces.Count: 1, ContainedTypes.Count: 0 };
					(@namespace, namespaceNode) = namespaceNode.NestedNamespaces.FirstOrDefault(), namespaceName += $".{@namespace.Name}"
				) ;

				builder.Append($$"""

					{{indentation}}namespace {{namespaceName}}
					{{indentation}}{
					""");

				namespaceNode.PrintMembers(builder, indentation + IndentationPrintPrefix);

				builder.Append($$"""
					{{indentation}}}

					""");
			}

			public readonly void PrintMembers(StringBuilder builder, string indentation)
			{
				foreach (var (type, typeNode) in ContainedTypes)
				{
					typeNode.Print(type, builder, indentation);
				}

				foreach (var (@namespace, namespaceNode) in NestedNamespaces)
				{
					namespaceNode.Print(@namespace, builder, indentation);
				}
			}
		}

		private readonly struct TypeNode()
		{
			public readonly Dictionary<INamedTypeSymbol, TypeNode> NestedTypes = new(SymbolEqualityComparer.Default);
			public readonly Dictionary<ISymbol, MemberOutput> ContainedMembers = new(SymbolEqualityComparer.Default);

			public readonly bool IsEmpty => NestedTypes.Count is not > 0 && ContainedMembers.Count is not > 0;

			public readonly bool Consolidate(Stack<INamespaceOrTypeSymbol> symbolBuffer)
			{
				var initialBufferSize = symbolBuffer.Count;

				foreach (var (type, typeNode) in NestedTypes)
				{
					if (typeNode.Consolidate(symbolBuffer))
					{
						symbolBuffer.Push(type);
					}
				}

				while (initialBufferSize < symbolBuffer.Count)
				{
					NestedTypes.Remove((INamedTypeSymbol)symbolBuffer.Pop());
				}

				return IsEmpty;
			}

			public readonly void Print(INamedTypeSymbol type, StringBuilder builder, string indentation)
			{
				builder.Append($$"""

					{{indentation}}partial {{type switch
					{
						{ TypeKind: TypeKind.Class, IsRecord: true } => "record ",
						{ TypeKind: TypeKind.Class } => "class ",
						{ TypeKind: TypeKind.Struct, IsRecord: true } => "record struct ",
						{ TypeKind: TypeKind.Struct } => "struct ",
						{ TypeKind: TypeKind.Interface } => "interface ",
						_ => string.Empty
					}}}{{type.Name}}{{(type.TypeParameters is { Length: > 0 } typeParameters ? $"<{string.Join(", ", typeParameters.Select(static param => param.Name))}>" : string.Empty)}}
					{{indentation}}{
					""");

				PrintMembers(builder, indentation + IndentationPrintPrefix);

				builder.Append($$"""
					{{indentation}}}

					""");
			}

			public readonly void PrintMembers(StringBuilder builder, string indentation)
			{
				foreach (var (_, memberOutput) in ContainedMembers)
				{
					memberOutput.Print(builder, indentation);
				}

				foreach (var (type, typeNode) in NestedTypes)
				{
					typeNode.Print(type, builder, indentation);
				}
			}
		}

		private readonly NamespaceNode mRootNamespace = new();

		public readonly bool IsEmpty => mRootNamespace.IsEmpty;

		public readonly void Consolidate() => mRootNamespace.Consolidate(new());

		public readonly void Print(StringBuilder builder) => mRootNamespace.PrintMembers(builder, string.Empty);

		public readonly bool TryAddMemberOutput(MemberOutput memberOutput, SourceProductionContext spc)
		{
			var containingType = memberOutput.Data.TargetSymbol.ContainingType;

			if (TryAddType(containingType, mRootNamespace, spc, out var typeNode))
			{
				if (!typeNode.ContainedMembers.TryGetValue(memberOutput.Data.TargetSymbol, out _))
				{
					typeNode.ContainedMembers.Add(memberOutput.Data.TargetSymbol, memberOutput);
				}

				return true;
			}

			return false;
		}

		private static bool TryAddType(INamedTypeSymbol type, NamespaceNode rootNamespace, SourceProductionContext spc, out TypeNode typeNode)
		{
			if ((type switch
			{
				{ ContainingType: not null }
					=> TryAddType(type.ContainingType, rootNamespace, spc, out var containingType) ? containingType.NestedTypes : null,

				not null
					=> TryAddNamespace(type.ContainingNamespace, rootNamespace, out var containingNamespace) ? containingNamespace.ContainedTypes : null,

				_ => null
			}) is { } typeContainer)
			{
				if (!typeContainer.TryGetValue(type!, out typeNode))
				{
					if (type is not { EnumUnderlyingType: null, DelegateInvokeMethod: null }
						|| !type.DeclaringSyntaxReferences.Any(syntaxRef => (syntaxRef.GetSyntax(spc.CancellationToken) as BaseTypeDeclarationSyntax)?.Modifiers.Any(SyntaxKind.PartialKeyword) is true))
					{
						spc.ReportDiagnostic(Diagnostic.Create(mUnsupportedTypeDeclarationSignatureDescriptor,
							location: type!.OriginalDefinition.Locations.FirstOrDefault(),
							type!.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
						));

						return false;
					}

					typeContainer.Add(type, typeNode = new());
				}

				return true;
			}

			Unsafe.SkipInit(out typeNode);
			return false;
		}

		private static bool TryAddNamespace(INamespaceSymbol @namespace, NamespaceNode rootNamespace, out NamespaceNode namespaceNode)
		{
			if (@namespace is { IsGlobalNamespace: false })
			{
				if (TryAddNamespace(@namespace.ContainingNamespace, rootNamespace, out var containingNamespace))
				{
					if (!containingNamespace.NestedNamespaces.TryGetValue(@namespace, out namespaceNode))
					{
						containingNamespace.NestedNamespaces.Add(@namespace, namespaceNode = new());
					}

					return true;
				}

				Unsafe.SkipInit(out namespaceNode);
				return false;
			}

			namespaceNode = rootNamespace;
			return true;
		}
	}

	private sealed class MemberOutput(FormattedConstantData data, string resultLiteral, ReturnTypeKind returnKind)
	{
		public FormattedConstantData Data { get; } = data;
		public string ResultLiteral { get; } = resultLiteral;
		public ReturnTypeKind ReturnKind { get; } = returnKind;

		public void Print(StringBuilder builder, string indentation)
		{
			if (Data.IsProperty)
			{
				var property = (IPropertySymbol)Data.TargetSymbol;
				builder.Append($$"""

					{{indentation}}[global::System.CodeDom.Compiler.GeneratedCode("{{mTool.Name}}", "{{mTool.Version}}")]
					{{indentation}}{{Data.TargetModifiers}} {{GetReturnTypeName(ReturnKind)}} {{property.Name}}
					{{indentation}}{
					{{indentation}}{{IndentationPrintPrefix}}[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining | global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
					{{indentation}}{{IndentationPrintPrefix}}get => {{ResultLiteral}};
					{{indentation}}}

					""");
			}
			else
			{
				var method = (IMethodSymbol)Data.TargetSymbol;
				var hasParameters = method.Parameters.Length > 0;

				if (hasParameters)
				{
					builder.Append($$"""

						#pragma warning disable IDE0060
						""");
				}

				builder.Append($$"""

					{{indentation}}[global::System.CodeDom.Compiler.GeneratedCode("{{mTool.Name}}", "{{mTool.Version}}")]
					{{indentation}}[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining | global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
					{{indentation}}{{Data.TargetModifiers}} {{GetReturnTypeName(ReturnKind)}} {{method.Name}}({{string.Join(", ", method.Parameters.Select(static p => $"{GetParameterModifiers(p)}{p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {p.Name}"))}}) => {{ResultLiteral}};
					""");

				if (hasParameters)
				{
					builder.Append("""

						#pragma warning restore IDE0060
						""");
				}

				builder.AppendLine();
			}
		}

		private static string GetReturnTypeName(ReturnTypeKind kind) => kind switch
		{
			ReturnTypeKind.String => "string",
			ReturnTypeKind.NullableString => "string? ",
			ReturnTypeKind.ReadOnlySpanChar => "global::System.ReadOnlySpan<char>",
			ReturnTypeKind.ReadOnlySpanByte => "global::System.ReadOnlySpan<byte>",
			_ => "string"
		};

		private static string GetParameterModifiers(IParameterSymbol parameter) => parameter.RefKind switch
		{
			RefKind.Ref => "ref ",
			RefKind.Out => "out ",
			RefKind.In => "in ",
			RefKind.RefReadOnlyParameter => "ref readonly ",
			_ => string.Empty
		};
	}
}
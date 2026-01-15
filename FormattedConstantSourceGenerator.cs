using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sdl3Sharp.SourceGeneration;

[Generator(LanguageNames.CSharp)]
internal sealed partial class FormattedConstantSourceGenerator : IIncrementalGenerator
{
	private const string DiagnosticDescriptorIdPrefix = "SDL3FMT";
	private const string DiagnosticDescriptorCategory = $"{nameof(Sdl3Sharp)}.{nameof(SourceGeneration)}";

	private static readonly (string Name, string Version) mTool = typeof(FormattedConstantSourceGenerator).Assembly.GetName() switch { var assemblyName => (assemblyName.Name, assemblyName.Version.ToString(3)) };

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		context.RegisterPostInitializationOutput(static pic => pic.AddEmbeddedAttributeDefinition());

		context.RegisterPostInitializationOutput(GenerateFormattedConstantAttributeSource);

		var formattedConstants = context.SyntaxProvider.ForAttributeWithMetadataName($"{FormattedConstantAttributeNamespaceName}.{FormattedConstantAttributeTypeName}",
			predicate: static (node, _) => node is MethodDeclarationSyntax or PropertyDeclarationSyntax,
			transform: static (gasc, cancellationToken) => FormattedConstantData.Create(gasc, cancellationToken)
		)
			.Where(static data => data is not null)
			.Select(static (data, _) => data!);

		context.RegisterImplementationSourceOutput(
			source: context.CompilationProvider.Combine(formattedConstants.Collect()),
			action: GenerateFormattedConstantsSource
		);
	}
}
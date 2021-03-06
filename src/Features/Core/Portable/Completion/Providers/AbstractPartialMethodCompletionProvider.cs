﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeGeneration;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.Completion.Providers
{
    internal abstract partial class AbstractPartialMethodCompletionProvider : AbstractMemberInsertingCompletionProvider
    {
        protected static readonly SymbolDisplayFormat SignatureDisplayFormat =
                new SymbolDisplayFormat(
                    genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                    memberOptions:
                        SymbolDisplayMemberOptions.IncludeParameters,
                    parameterOptions:
                        SymbolDisplayParameterOptions.IncludeName |
                        SymbolDisplayParameterOptions.IncludeType |
                        SymbolDisplayParameterOptions.IncludeParamsRefOut,
                    miscellaneousOptions:
                        SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                        SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        protected AbstractPartialMethodCompletionProvider()
        {
        }

        protected abstract bool IsPartialMethodCompletionContext(SyntaxTree tree, int position, CancellationToken cancellationToken, out DeclarationModifiers modifiers, out SyntaxToken token);
        protected abstract string GetDisplayText(IMethodSymbol method, SemanticModel semanticModel, int position);
        protected abstract bool IsPartial(IMethodSymbol method);

        public override async Task ProvideCompletionsAsync(CompletionContext context)
        {
            var document = context.Document;
            var position = context.Position;
            var cancellationToken = context.CancellationToken;

            var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            if (!IsPartialMethodCompletionContext(tree, position, cancellationToken, out var modifiers, out var token))
            {
                return;
            }

            var items = await CreatePartialItemsAsync(
                document, position, context.CompletionListSpan, modifiers, token, cancellationToken).ConfigureAwait(false);

            if (items?.Any() == true)
            {
                context.IsExclusive = true;
                context.AddItems(items);
            }
        }

        protected override async Task<ISymbol> GenerateMemberAsync(ISymbol member, INamedTypeSymbol containingType, Document document, CompletionItem item, CancellationToken cancellationToken)
        {
            var syntaxFactory = document.GetLanguageService<SyntaxGenerator>();
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

            return CodeGenerationSymbolFactory.CreateMethodSymbol(attributes: new List<AttributeData>(),
                accessibility: Accessibility.NotApplicable,
                modifiers: MemberInsertionCompletionItem.GetModifiers(item),
                returnType: semanticModel.Compilation.GetSpecialType(SpecialType.System_Void),
                explicitInterfaceSymbol: null,
                name: member.Name,
                typeParameters: ((IMethodSymbol)member).TypeParameters,
                parameters: member.GetParameters().Select(p => CodeGenerationSymbolFactory.CreateParameterSymbol(p.GetAttributes(), p.RefKind, p.IsParams, p.Type, p.Name)).ToList(),
                statements: syntaxFactory.CreateThrowNotImplementedStatementBlock(semanticModel.Compilation));
        }

        protected async Task<IEnumerable<CompletionItem>> CreatePartialItemsAsync(
            Document document, int position, TextSpan span, DeclarationModifiers modifiers, SyntaxToken token, CancellationToken cancellationToken)
        {
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var enclosingSymbol = semanticModel.GetEnclosingSymbol(position, cancellationToken) as INamedTypeSymbol;

            // Only inside classes and structs
            if (enclosingSymbol == null || !(enclosingSymbol.TypeKind == TypeKind.Struct || enclosingSymbol.TypeKind == TypeKind.Class))
            {
                return null;
            }

            var symbols = semanticModel.LookupSymbols(position, container: enclosingSymbol)
                                        .OfType<IMethodSymbol>()
                                        .Where(m => IsPartial(m) && m.PartialImplementationPart == null);

            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var line = text.Lines.IndexOf(position);
            var lineSpan = text.Lines.GetLineFromPosition(position).Span;
            return symbols.Select(s => CreateItem(s, line, lineSpan, span, semanticModel, modifiers, document, token));
        }

        private CompletionItem CreateItem(IMethodSymbol method, int line, TextSpan lineSpan, TextSpan span, SemanticModel semanticModel, DeclarationModifiers modifiers, Document document, SyntaxToken token)
        {
            modifiers = new DeclarationModifiers(method.IsStatic, isUnsafe: method.IsUnsafe(), isPartial: true, isAsync: modifiers.IsAsync);
            var displayText = GetDisplayText(method, semanticModel, span.Start);

            return MemberInsertionCompletionItem.Create(
                displayText,
                Glyph.MethodPrivate,
                modifiers,
                line,
                method,
                token,
                span.Start,
                rules: this.GetRules());
        }
    }
}

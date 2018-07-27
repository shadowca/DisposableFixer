﻿using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DisposableFixer.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

namespace DisposableFixer.CodeFix
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UndisposedPropertyCodeFix)), Shared]
    public class UndisposedPropertyCodeFix : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds { get; } =
            ImmutableArray.Create(
                SyntaxNodeAnalysisContextExtension.IdForAssignmendFromMethodInvocationToPropertyNotDisposed,
                SyntaxNodeAnalysisContextExtension.IdForAssignmendFromObjectCreationToPropertyNotDisposed,
                SyntaxNodeAnalysisContextExtension.IdForAssignmendFromMethodInvocationToFieldNotDisposed,
                SyntaxNodeAnalysisContextExtension.IdForAssignmendFromObjectCreationToFieldNotDisposed
                );

        public override Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            RegisterCodeActionsForUndisposedProperties(context);
            RegisterCodeActionsForUndisposedFields(context);
            
            return Task.FromResult(1);
        }

        private static void RegisterCodeActionsForUndisposedProperties(CodeFixContext context)
        {
            var id = context.Diagnostics.First().Id;
            if (id == SyntaxNodeAnalysisContextExtension.IdForAssignmendFromObjectCreationToPropertyNotDisposed
                || id  == SyntaxNodeAnalysisContextExtension.IdForAssignmendFromMethodInvocationToPropertyNotDisposed) {
                context.RegisterCodeFix(
                    CodeAction.Create("Dispose property in Dispose() method", c => CreateDisposeCallInParameterlessDisposeMethod(context, c)),
                    context.Diagnostics);
            }
        }

        private static void RegisterCodeActionsForUndisposedFields(CodeFixContext context) {
            var id = context.Diagnostics.First().Id;
            if (id == SyntaxNodeAnalysisContextExtension.IdForAssignmendFromObjectCreationToFieldNotDisposed
                || id == SyntaxNodeAnalysisContextExtension.IdForAssignmendFromMethodInvocationToFieldNotDisposed) {
                context.RegisterCodeFix(
                    CodeAction.Create("Dispose field in Dispose() method", c => CreateDisposeCallInParameterlessDisposeMethod(context, c)),
                    context.Diagnostics);
            }
        }

        private static async Task<Document> CreateDisposeCallInParameterlessDisposeMethod(CodeFixContext context, CancellationToken cancel)
        {
            var oldRoot = await context.Document.GetSyntaxRootAsync(cancel);
            var node = oldRoot.FindNode(context.Span);
            var variableName = context.Diagnostics.First().Properties[Constants.Variablename];

            var editor = await DocumentEditor.CreateAsync(context.Document, context.CancellationToken);

            if (node.TryFindParentClass(out var oldClass))
            {
                var model = await context.Document.GetSemanticModelAsync(cancel);
                var containingSymbol = model.GetEnclosingSymbol(context.Span.Start, cancel).ContainingSymbol;
                var @classtype =
                    containingSymbol as INamedTypeSymbol;
                if (@classtype == null) return context.Document;


                if (!(oldClass.BaseList != null && oldClass.BaseList.Types.Any(bts =>
                         bts.DescendantNodes<IdentifierNameSyntax>().Any(ins => ins.Identifier.Text == "IDisposable"))))
                {
                    var disposeDeclaration = SyntaxFactory.IdentifierName("IDisposable");
                    editor.AddBaseType(oldClass, disposeDeclaration);
                }

                var disposeMethods = oldClass
                    .DescendantNodes<MethodDeclarationSyntax>()
                    .Where(mds => mds.Identifier.Text == Constants.Dispose && mds.ParameterList.Parameters.Count == 0)
                    .ToArray();

                if (disposeMethods.Any())
                {
                    var oldDisposeMethod = disposeMethods.First();
                    var oldStatements = oldDisposeMethod.Body.Statements;
                    var newStatements = oldStatements
                        .Add(SyntaxFactory.ExpressionStatement(
                            SyntaxFactory.ConditionalAccessExpression(
                                SyntaxFactory.IdentifierName(variableName),
                                SyntaxFactory.InvocationExpression(
                                    SyntaxFactory.MemberBindingExpression(
                                        SyntaxFactory.IdentifierName(Constants.Dispose))))));
                    var newDisposeMethod = SyntaxFactory
                        .MethodDeclaration(
                            SyntaxFactory.PredefinedType(
                                SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                            SyntaxFactory.Identifier(Constants.Dispose))
                        .WithModifiers(
                            SyntaxFactory.TokenList(
                                SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                        .WithBody(SyntaxFactory.Block(newStatements))
                        .WithoutAnnotations(Formatter.Annotation);
                    editor.ReplaceNode(oldDisposeMethod, newDisposeMethod);
                }
                else
                {
                    var disposeMethod = SyntaxFactory
                        .MethodDeclaration(
                            SyntaxFactory.PredefinedType(
                                SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                            SyntaxFactory.Identifier(Constants.Dispose))
                        .WithModifiers(
                            SyntaxFactory.TokenList(
                                SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                        .WithBody(
                            SyntaxFactory.Block(
                                SyntaxFactory.SingletonList<StatementSyntax>(
                                    SyntaxFactory.ExpressionStatement(
                                        SyntaxFactory.ConditionalAccessExpression(
                                            SyntaxFactory.IdentifierName(variableName),
                                            SyntaxFactory.InvocationExpression(SyntaxFactory.MemberBindingExpression(SyntaxFactory.IdentifierName(Constants.Dispose))))).WithSemicolonToken(SyntaxFactory.MissingToken(SyntaxKind.SemicolonToken)))))
                        .NormalizeWhitespace();
                    editor.AddMember(oldClass,disposeMethod);
                }

                var usings = oldRoot.DescendantNodes<UsingDirectiveSyntax>()
                    .ToArray();
                var systemImport = usings
                    .SelectMany(uds => uds.DescendantNodes<QualifiedNameSyntax>())
                    .Select(qns => qns.DescendantNodes<IdentifierNameSyntax>().ToArray())
                    .FirstOrDefault(inss => inss.Length == 1 && inss[0].Identifier.Text == "System");

                if (systemImport == null)
                {
                    var newSystemImport = SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName("System"));
                    editor.InsertAfter(usings.Last(), new []{ newSystemImport });
                }
            }
            return editor.GetChangedDocument();
        }
    }
}
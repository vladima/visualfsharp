﻿// Copyright (c) Microsoft Corporation.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.VisualStudio.FSharp.Editor

open System
open System.Composition
open System.Collections.Generic
open System.Collections.Immutable
open System.Linq
open System.Threading
open System.Threading.Tasks
open System.Runtime.CompilerServices

open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.Classification
open Microsoft.CodeAnalysis.Editor
open Microsoft.CodeAnalysis.Editor.Host
open Microsoft.CodeAnalysis.Navigation
open Microsoft.CodeAnalysis.Editor.Shared.Utilities
open Microsoft.CodeAnalysis.Host.Mef
open Microsoft.CodeAnalysis.Text

open Microsoft.VisualStudio.FSharp.LanguageService
open Microsoft.VisualStudio.Text
open Microsoft.VisualStudio.Text.Tagging

open Microsoft.FSharp.Compiler.Range
open Microsoft.FSharp.Compiler.SourceCodeServices

type internal FSharpNavigableItem(document: Document, textSpan: TextSpan) =

    interface INavigableItem with
        member this.Glyph = Glyph.BasicFile
        member this.DisplayFileLocation = true
        member this.IsImplicitlyDeclared = false
        member this.Document = document
        member this.SourceSpan = textSpan
        member this.DisplayTaggedParts = ImmutableArray<TaggedText>.Empty
        member this.ChildItems = ImmutableArray<INavigableItem>.Empty

[<Shared>]
[<ExportLanguageService(typeof<IGoToDefinitionService>, FSharpCommonConstants.FSharpLanguageName)>]
type internal FSharpGoToDefinitionService 
    [<ImportingConstructor>]
    (
        checkerProvider: FSharpCheckerProvider,
        projectInfoManager: ProjectInfoManager,
        [<ImportMany>]presenters: IEnumerable<INavigableItemsPresenter>
    ) =

    static member FindDefinition(checker: FSharpChecker, documentKey: DocumentId, sourceText: SourceText, filePath: string, position: int, defines: string list, options: FSharpProjectOptions, textVersionHash: int) : Async<Option<range>> = 
        async {
            let textLine = sourceText.Lines.GetLineFromPosition(position)
            let textLinePos = sourceText.Lines.GetLinePosition(position)
            let fcsTextLineNumber = textLinePos.Line + 1 // Roslyn line numbers are zero-based, FSharp.Compiler.Service line numbers are 1-based
            match CommonHelpers.getSymbolAtPosition(documentKey, sourceText, position, filePath, defines, SymbolLookupKind.Fuzzy) with 
            | Some symbol -> 
                let! _parseResults, checkFileAnswer = checker.ParseAndCheckFileInProject(filePath, textVersionHash, sourceText.ToString(), options)
                match checkFileAnswer with
                | FSharpCheckFileAnswer.Aborted -> return None
                | FSharpCheckFileAnswer.Succeeded(checkFileResults) -> 
            
                let! declarations = checkFileResults.GetDeclarationLocationAlternate (fcsTextLineNumber, symbol.RightColumn, textLine.ToString(), [symbol.Text], false)
            
                match declarations with
                | FSharpFindDeclResult.DeclFound(range) -> return Some(range)
                | _ -> return None
            | None -> return None
        }
    
    // FSROSLYNTODO: Since we are not integrated with the Roslyn project system yet, the below call
    // document.Project.Solution.GetDocumentIdsWithFilePath() will only access files in the same project.
    // Either Roslyn INavigableItem needs to be extended to allow arbitary full paths, or we need to
    // fully integrate with their project system.
    member this.FindDefinitionsAsyncAux(document: Document, position: int, cancellationToken: CancellationToken) =
        async {
            let results = List<INavigableItem>()
            match projectInfoManager.TryGetOptionsForEditingDocumentOrProject(document)  with 
            | Some options ->
                let! sourceText = document.GetTextAsync(cancellationToken) |> Async.AwaitTask
                let! textVersion = document.GetTextVersionAsync(cancellationToken) |> Async.AwaitTask
                let defines = CompilerEnvironment.GetCompilationDefinesForEditing(document.Name, options.OtherOptions |> Seq.toList)
                let! definition = FSharpGoToDefinitionService.FindDefinition(checkerProvider.Checker, document.Id, sourceText, document.FilePath, position, defines, options, textVersion.GetHashCode())

                match definition with
                | Some(range) ->
                    // REVIEW: 
                    let fileName = try System.IO.Path.GetFullPath(range.FileName) with _ -> range.FileName
                    let refDocumentIds = document.Project.Solution.GetDocumentIdsWithFilePath(fileName)
                    if not refDocumentIds.IsEmpty then 
                        let refDocumentId = refDocumentIds.First()
                        let refDocument = document.Project.Solution.GetDocument(refDocumentId)
                        let! refSourceText = refDocument.GetTextAsync(cancellationToken) |> Async.AwaitTask
                        let refTextSpan = CommonRoslynHelpers.FSharpRangeToTextSpan(refSourceText, range)
                        results.Add(FSharpNavigableItem(refDocument, refTextSpan))

                | None -> ()
            | None -> ()
            return results.AsEnumerable()
         } |> CommonRoslynHelpers.StartAsyncAsTask cancellationToken

    interface IGoToDefinitionService with
        member this.FindDefinitionsAsync(document: Document, position: int, cancellationToken: CancellationToken) =
            this.FindDefinitionsAsyncAux(document, position, cancellationToken)

        member this.TryGoToDefinition(document: Document, position: int, cancellationToken: CancellationToken) =
            let definitionTask = this.FindDefinitionsAsyncAux(document, position, cancellationToken)
            
            // REVIEW: document this use of a blocking wait on the cancellation token, explaining why it is ok
            definitionTask.Wait(cancellationToken)
            
            if definitionTask.Status = TaskStatus.RanToCompletion && definitionTask.Result.Any() then
                let navigableItem = definitionTask.Result.First() // F# API provides only one INavigableItem
                let workspace = document.Project.Solution.Workspace
                let navigationService = workspace.Services.GetService<IDocumentNavigationService>()
                ignore presenters
                navigationService.TryNavigateToSpan(workspace, navigableItem.Document.Id, navigableItem.SourceSpan)

                // FSROSLYNTODO: potentially display multiple results here
                // If GotoDef returns one result then it should try to jump to a discovered location. If it returns multiple results then it should use 
                // presenters to render items so user can choose whatever he needs. Given that per comment F# API always returns only one item then we 
                // should always navigate to definition and get rid of presenters.
                //
                //let refDisplayString = refSourceText.GetSubText(refTextSpan).ToString()
                //for presenter in presenters do
                //    presenter.DisplayResult(navigableItem.DisplayString, definitionTask.Result)
                //true

            else
                false

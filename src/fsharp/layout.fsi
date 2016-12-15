// Copyright (c) Microsoft Corporation.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

module internal Microsoft.FSharp.Compiler.Layout

open System.Text
open System.IO
open Internal.Utilities.StructuredFormat

type layout = Internal.Utilities.StructuredFormat.Layout
type TaggedText = Internal.Utilities.StructuredFormat.TaggedText

val emptyL                : Layout
val isEmptyL              : Layout -> bool
  
val wordL                 : TaggedText -> Layout
val sepL                  : TaggedText -> Layout
val rightL                : TaggedText -> Layout
val leftL                 : TaggedText -> Layout
val tagKeyword:     s: string -> TaggedText
val tagIdentifier:  s: string -> TaggedText
val tagPunctuation: s: string -> TaggedText
val tagNumber:      s: string -> TaggedText
val tagString:      s: string -> TaggedText
val tagType:        s: string -> TaggedText
val tagText:        s: string -> TaggedText
val tagComment:     s: string -> TaggedText
val ( ^^ )                : Layout -> Layout -> Layout   (* never break "glue" *)
val ( ++ )                : Layout -> Layout -> Layout   (* if break, indent=0 *)
val ( -- )                : Layout -> Layout -> Layout   (* if break, indent=1 *)
val ( --- )               : Layout -> Layout -> Layout   (* if break, indent=2 *)
val ( ---- )              : Layout -> Layout -> Layout   (* if break, indent=2 *)
val ( ----- )             : Layout -> Layout -> Layout   (* if break, indent=2 *)
val ( @@ )                : Layout -> Layout -> Layout   (* broken ident=0 *)
val ( @@- )               : Layout -> Layout -> Layout   (* broken ident=1 *)
val ( @@-- )              : Layout -> Layout -> Layout   (* broken ident=2 *)

val commaListL            : Layout list -> Layout
val spaceListL            : Layout list -> Layout
val semiListL             : Layout list -> Layout
val sepListL              : Layout -> Layout list -> Layout

val bracketL              : Layout -> Layout
val tupleL                : Layout list -> Layout
val aboveL                : Layout -> Layout -> Layout
val aboveListL            : Layout list -> Layout

val optionL               : ('a -> Layout) -> 'a option -> Layout    
val listL                 : ('a -> Layout) -> 'a list   -> Layout

val squashTo              : int -> Layout -> Layout

val showL                 : Layout -> string
val outL                  : TextWriter -> Layout -> unit
val bufferL               : StringBuilder -> Layout -> unit

/// render a Layout yielding an 'a using a 'b (hidden state) type 
type LayoutRenderer<'a,'b> =
    abstract Start    : unit -> 'b
    abstract AddText  : 'b -> TaggedText -> 'b
    abstract AddBreak : 'b -> int -> 'b
    abstract AddTag   : 'b -> string * (string * string) list * bool -> 'b
    abstract Finish   : 'b -> 'a

type NoState = NoState
type NoResult = NoResult

/// Run a render on a Layout       
val renderL  : LayoutRenderer<'b,'a> -> Layout -> 'b

/// Primitive renders 
val stringR  : LayoutRenderer<string,string list>
val channelR : TextWriter -> LayoutRenderer<NoResult,NoState>
val bufferR  : StringBuilder -> LayoutRenderer<NoResult,NoState>
val taggedTextListR  : LayoutRenderer<list<TaggedText>, list<TaggedText>>


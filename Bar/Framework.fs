﻿module Bar
open System
open System.IO
open System.Collections.Generic
open System.Reflection
open System.Threading.Tasks
open Owin

let getMethods verb (methods:seq<MethodInfo>) =
    methods
    |> Seq.where (fun x -> x.Name.StartsWith(verb + " /"))
    |> Seq.map (fun x -> (x.Name.Replace(verb + " ", ""), x))

let headToOption list =
    match Seq.length list with
    | i when i > 0 -> Seq.head list|> Some
    | _ -> None

let getMethod requestPath methods =
    methods
    |> Seq.where (fun (path, _) -> path = requestPath)
    |> Seq.map (fun (_,response) -> response)
    |> headToOption

let parseQueryString (queryString:string) =
    queryString.Split '&'
    |> Seq.map (fun x -> ((x.Split '=').[0], (x.Split '=').[1]))

let getParameter parameters (x:ParameterInfo) =
    parameters
    |> Seq.find (fun (name,_)-> name = x.Name)
    |> (fun (_,z) -> (z, x.ParameterType))

let invokeMethod instance (methodInfo:MethodInfo) parseParameter =
    let parameters =
        methodInfo.GetParameters()
        |> Seq.map parseParameter
        |> Seq.toArray
    methodInfo.Invoke(instance, parameters)

let (<<) f g x = f(g x)

let useBar instance next (converter:string*Type->obj) (enviroment:IDictionary<string,obj>) =
    let requestMethod = enviroment.["owin.RequestMethod"] :?> string
    let requestPath = enviroment.["owin.RequestPath"] :?> string
    let queryString = enviroment.["owin.RequestQueryString"] :?> string
    let discoveredMethod =
        instance.GetType().GetMethods()
        |> getMethods requestMethod
        |> getMethod requestPath
    match discoveredMethod with
    | Some(invokingMethod) ->
        let requestBody = (new StreamReader (enviroment.["owin.RequestBody"] :?> Stream)).ReadToEnd()
        let response =
            parseQueryString queryString
            |> Seq.append [("body", requestBody)]
            |> getParameter
            |> (fun x -> converter << x)
            |> invokeMethod instance invokingMethod
        enviroment.Add("bar.RawResponse", response) |> ignore
        Task.Run (fun () -> next enviroment)
    | None -> Task.Run (fun () -> next enviroment)
    
let Func2 (x:Func<_,_>) y = x.Invoke(y)

type IAppBuilder with
    member x.UseBar (instance, converter) =
        x.Use(fun next -> useBar instance (Func2 next) converter)
﻿open System
open System.Net

open Suave
open Suave.Web
open Suave.Http
open Suave.Http.Applicatives
open Suave.Http.Files
open Suave.Http.Successful
open Suave.Types
open Suave.Session
open Suave.Log
open System.IO
open System.Text

open System.Data
open ServiceStack.DataAnnotations
open ServiceStack.OrmLite
open ServiceStack.OrmLite.Sqlite

open Newtonsoft.Json
open Newtonsoft.Json.Converters

<% _.each(entities, function (entity) { %>
[<CLIMutable>]
[<JsonObject(MemberSerialization.OptIn)>]
type <%= _.capitalize(entity.name) %> = {
  [<AutoIncrement>]
  [<JsonProperty(PropertyName = "id", NullValueHandling = NullValueHandling.Ignore)>]
  mutable Id : int
  <% _.each(entity.attrs, function (attr) { %>
  [<JsonProperty("<%= attr.attrName %>")>]
  mutable <%= _.capitalize(attr.attrName) %> : <%= attr.attrImplType %><% }); %>
}<% }); %>

let logger = Loggers.sane_defaults_for Debug

let dbFactory =
  let dbConnectionFactory = new OrmLiteConnectionFactory("my.db", SqliteDialect.Provider)
  use db = dbConnectionFactory.OpenDbConnection()<% _.each(entities, function (entity) { %>
  db.CreateTable<<%= _.capitalize(entity.name) %>>(false)<% }); %>
  dbConnectionFactory

type CustomDateTimeConverter() =
  inherit IsoDateTimeConverter()

  do base.DateTimeFormat <- "yyyy-MM-dd"

let converters : JsonConverter[] = [| CustomDateTimeConverter() |]

/// Convert the object to a JSON representation inside a byte array (can be made string of)
let to_json<'a> (o: 'a) =
  Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(o, converters))

/// Transform the byte array representing a JSON object to a .Net object
let from_json<'a> (bytes:byte []) =
  JsonConvert.DeserializeObject<'a>(Encoding.UTF8.GetString(bytes), converters)

/// Expose function f through a json call; lets you write like
///
/// let app =
///   url "/path" >>= request (map_json some_function);
///
let map_json f (r : Suave.Types.HttpRequest) =
  f (from_json(r.raw_form)) |> to_json |> Successful.ok

<% _.each(entities, function (entity) { %>
let <%= entity.name %>Part : WebPart =
  choose [
    GET >>= url "/<%= baseName %>/<%= pluralize(entity.name) %>" >>= request(
      (fun _ -> 
        use db = dbFactory.OpenDbConnection()
        let rows = db.Select<<%= _.capitalize(entity.name) %>>()
        to_json rows |> Successful.ok));

    GET >>= url_scan "/<%= baseName %>/<%= pluralize(entity.name) %>/%d"
      (fun id -> 
        use db = dbFactory.OpenDbConnection()
        let row = db.Single<<%= _.capitalize(entity.name) %>>(fun r -> r.Id = id)
        to_json row |> Successful.ok);

    POST >>= url "/<%= baseName %>/<%= pluralize(entity.name) %>" >>= request(map_json 
      (fun (row : <%= _.capitalize(entity.name) %>) -> 
        use db = dbFactory.OpenDbConnection()
        let num = db.Insert(row)
        row.Id <- int(db.LastInsertId())
        row));

    PUT >>= url_scan "/<%= baseName %>/<%= pluralize(entity.name) %>/%d"
      (fun id -> request(map_json (fun (row : <%= _.capitalize(entity.name) %>) -> 
        row.Id <- id
        use db = dbFactory.OpenDbConnection()
        let old = db.Single<<%= _.capitalize(entity.name) %>>(fun r -> r.Id = id)
        let num = db.Update(row)
        row)));

    DELETE >>= url_scan "/<%= baseName %>/<%= pluralize(entity.name) %>/%d"
      (fun id -> 
        use db = dbFactory.OpenDbConnection()
        let num = db.Delete<<%= _.capitalize(entity.name) %>>(fun r -> r.Id = id)
        Successful.no_content);
  ]<% }); %>

choose [
  log logger log_format >>= never
  GET >>= url "/" >>= browse_file "index.html";
  <% _.each(entities, function (entity) { %>
  <%= entity.name %>Part; <% }); %>
  GET >>= browse; //serves file if it exists
  RequestErrors.NOT_FOUND "Found no handlers"
  ]
  |> web_server
      { bindings =
        [ HttpBinding.Create(HTTP, "127.0.0.1", 8080) ]
      ; error_handler    = default_error_handler
      ; web_part_timeout = TimeSpan.FromMilliseconds 1000.
      ; listen_timeout   = TimeSpan.FromMilliseconds 2000.
      ; ct               = Async.DefaultCancellationToken
      ; buffer_size      = 2048
      ; max_ops          = 100
      ; mime_types_map   = Writers.default_mime_types_map
      ; home_folder      = Some(Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "Content"))
      ; compressed_files_folder = None
      ; logger           = logger }


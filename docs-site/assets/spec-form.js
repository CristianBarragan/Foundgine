/*!
 * Foundgine app spec form: embeddable component (Shadow DOM, no dependencies).
 *
 * Usage:
 *   <div id="foundgine-spec-form"></div>
 *   (script tag with src pointing at this file, loaded after the div)
 *
 * Any element with id="foundgine-spec-form" or the attribute data-foundgine-spec-form is mounted.
 * Optional attributes / CSS variables on the container:
 *   data-theme="light|dark"          force a theme (default follows prefers-color-scheme)
 *   --fsf-sticky-top: 64px           offset for a sticky site header
 *   --fsf-accent, --fsf-bg, --fsf-panel, --fsf-ink, --fsf-mute, --fsf-line, --fsf-warn, --fsf-font
 * API: window.FoundgineSpecForm.mount(element), window.FoundgineSpecForm.mountAll()
 */
(function () {
  "use strict";
  var CSS = ":host{display:block;--fsf-top:var(--fsf-sticky-top,0px)}\n*{box-sizing:border-box}\n.fsf{\n  --d-bg:#fafaf8;--d-panel:#fff;--d-ink:#1d1d1b;--d-mute:#6b6a65;--d-line:#e3e1da;--d-accent:#0f6e56;--d-accent-ink:#fff;--d-warn:#b4531a;--d-soft:#f1efe8;\n  --bg:var(--fsf-bg,var(--d-bg));--panel:var(--fsf-panel,var(--d-panel));--ink:var(--fsf-ink,var(--d-ink));--mute:var(--fsf-mute,var(--d-mute));\n  --line:var(--fsf-line,var(--d-line));--accent:var(--fsf-accent,var(--d-accent));--accent-ink:var(--fsf-accent-ink,var(--d-accent-ink));\n  --warn:var(--fsf-warn,var(--d-warn));--soft:var(--fsf-soft,var(--d-soft));\n  --font:var(--fsf-font,system-ui,-apple-system,Segoe UI,Roboto,sans-serif);\n  background:var(--bg);color:var(--ink);font:15px/1.5 var(--font);border:1px solid var(--line);border-radius:12px;position:relative\n}\n@media (prefers-color-scheme:dark){:host(:not([data-theme=light])) .fsf{--d-bg:#161614;--d-panel:#1f1f1c;--d-ink:#ecebe6;--d-mute:#9a988f;--d-line:#34332e;--d-accent:#3ec59b;--d-accent-ink:#0b1f18;--d-warn:#f0a066;--d-soft:#282824}}\n:host([data-theme=dark]) .fsf{--d-bg:#161614;--d-panel:#1f1f1c;--d-ink:#ecebe6;--d-mute:#9a988f;--d-line:#34332e;--d-accent:#3ec59b;--d-accent-ink:#0b1f18;--d-warn:#f0a066;--d-soft:#282824}\nheader{position:sticky;top:var(--fsf-top);z-index:5;background:var(--panel);border-bottom:1px solid var(--line);padding:10px 16px;display:flex;gap:12px;align-items:center;flex-wrap:wrap;border-radius:12px 12px 0 0}\nheader h1{font-size:16px;margin:0;font-weight:600}\n.bar{flex:1;min-width:120px;height:6px;background:var(--soft);border-radius:3px;overflow:hidden}\n.bar i{display:block;height:100%;background:var(--accent);width:0;transition:width .2s}\n.small{font-size:12.5px;color:var(--mute)}\nbutton{font:inherit;cursor:pointer;border:1px solid var(--line);background:var(--panel);color:var(--ink);border-radius:8px;padding:6px 12px}\nbutton:hover{background:var(--soft)}\nbutton.primary{background:var(--accent);color:var(--accent-ink);border-color:var(--accent);font-weight:600}\nbutton.x{padding:2px 8px;color:var(--mute)}\n.wrap{display:grid;grid-template-columns:220px 1fr;gap:20px;padding:16px}\nnav{position:sticky;top:calc(var(--fsf-top) + 64px);align-self:start;max-height:calc(100vh - 90px);overflow:auto;font-size:13.5px}\nnav a{display:flex;justify-content:space-between;gap:6px;padding:5px 8px;border-radius:6px;color:var(--ink);text-decoration:none}\nnav a:hover{background:var(--soft)}\nnav a b{font-weight:500;color:var(--mute)}\nnav a.done b{color:var(--accent)}\n.fsf.narrow .wrap{grid-template-columns:1fr}\n.fsf.narrow nav{display:none}\nsection{background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:16px 18px;margin-bottom:16px;scroll-margin-top:calc(var(--fsf-top) + 70px)}\nsection h2{margin:0 0 2px;font-size:17px;font-family:inherit;border:0;padding:0}\nsection>p.intro{margin:0 0 14px;color:var(--mute);font-size:13.5px}\n.f{margin:0 0 12px}\n.f>label{display:block;font-weight:600;font-size:13.5px;margin-bottom:3px}\n.f .h{font-size:12.5px;color:var(--mute);margin:0 0 4px}\n.req::after{content:\" *\";color:var(--warn)}\ninput[type=text],textarea,select{width:100%;font:inherit;color:var(--ink);background:var(--bg);border:1px solid var(--line);border-radius:8px;padding:7px 9px}\ntextarea{min-height:64px;resize:vertical}\ntextarea.code{font-family:ui-monospace,Menlo,Consolas,monospace;font-size:13px;min-height:90px}\ninput:focus,textarea:focus,select:focus{outline:2px solid var(--accent);outline-offset:-1px}\n.chk{display:flex;flex-wrap:wrap;gap:6px 14px}\n.chk label{font-weight:400;display:flex;gap:6px;align-items:flex-start;font-size:14px}\n.card{border:1px solid var(--line);border-radius:10px;padding:12px;margin:0 0 10px;background:var(--bg)}\n.card .hd{display:flex;justify-content:space-between;align-items:center;margin-bottom:8px}\n.card .hd b{font-size:13px;color:var(--mute)}\n.card .card{background:var(--panel)}\n.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr));gap:0 12px}\n.grid .wide{grid-column:1/-1}\n#out{display:none;position:fixed;inset:0;background:rgba(0,0,0,.5);z-index:2147483000;padding:20px;overflow:auto}\n#out .box{background:var(--panel);color:var(--ink);max-width:900px;margin:0 auto;border-radius:12px;padding:16px;border:1px solid var(--line)}\n#out textarea{min-height:52vh;font-family:ui-monospace,Menlo,Consolas,monospace;font-size:12.5px}\n.row{display:flex;gap:8px;flex-wrap:wrap;align-items:center;margin:8px 0}\n.warnbox{background:var(--soft);border-left:3px solid var(--warn);padding:8px 12px;border-radius:6px;font-size:13px;margin:8px 0}\n.tabs button.on{background:var(--accent);color:var(--accent-ink);border-color:var(--accent)}";
  var HTML = "<div class=\"fsf\" id=\"fsf\"><header>\n  <h1>Foundgine app spec form</h1>\n  <div class=\"bar\"><i id=\"bar\"></i></div>\n  <span class=\"small\" id=\"pct\">0% required filled</span>\n  <button id=\"bExample\">Load example</button>\n  <button id=\"bImport\">Import JSON</button>\n  <button id=\"bReset\">Clear</button>\n  <button class=\"primary\" id=\"bGen\">Generate AI prompt</button>\n</header>\n<div class=\"wrap\">\n  <nav id=\"nav\"></nav>\n  <main id=\"main\"></main>\n</div>\n\n<div id=\"out\"><div class=\"box\">\n  <div class=\"row tabs\"><button id=\"tPrompt\" class=\"on\">AI prompt (paste into Claude)</button><button id=\"tJson\">JSON (save / re-import)</button><button id=\"tImp\">Import</button><span style=\"flex:1\"></span><button id=\"bClose\">Close</button></div>\n  <div id=\"warn\"></div>\n  <textarea id=\"outText\" spellcheck=\"false\"></textarea>\n  <div class=\"row\"><button class=\"primary\" id=\"bCopy\">Copy</button><span class=\"small\" id=\"copyMsg\"></span></div>\n  <div class=\"small\">Downloads are blocked in this viewer, so copy the text. Paste the prompt into a new chat together with your Foundgine zip, or keep the JSON to re-import later.</div>\n</div></div></div>";

// ---------- helpers for schema ----------
const T=(key,label,o={})=>Object.assign({t:'text',key,label},o);
const A=(key,label,o={})=>Object.assign({t:'area',key,label},o);
const S=(key,label,options,o={})=>Object.assign({t:'select',key,label,options},o);
const M=(key,label,options,o={})=>Object.assign({t:'multi',key,label,options},o);
const C=(key,label,o={})=>Object.assign({t:'check',key,label},o);
const L=(key,label,item,fields,o={})=>Object.assign({t:'list',key,label,item,fields},o);
const AI='Let AI decide';

const SECTIONS=[
{id:'project',title:'1. Project',intro:'What you are generating, in which language, and where it lives.',items:[
  T('name','Application name',{req:1,hint:'Used for the project, class prefixes and docs, e.g. "EquipmentRental".'}),
  A('purpose','What does it do, in 2-4 sentences?',{req:1,hint:'The business problem and who gets value from an agent being able to ask questions or act.'}),
  S('language','Language / runtime',['.NET 10 (C#)  - reference sample: Foundgine.SupplyChain.Advanced','Java 27 (Maven)  - port: foundgine-supply-chain-advanced','Both: .NET first, then a Java port with the same model and behaviour'],{req:1,hint:'Foundgine is one architecture in two languages. The .NET sample is the reference; the Java sample mirrors it.'}),
  S('shape','Deliverable shape',['New sample project inside the Foundgine repo (like the Advanced supply-chain samples)','Standalone project depending on published packages (NuGet / Maven)','ASP.NET Core app (.NET)','Spring Boot app (Java, Foundgine Spring auto-configuration)'],{req:1}),
  S('modelStyle','How the semantic model is declared',['Fluent configuration (FoundgineModel; sample default)','Attributes / annotations','AOT source generator (Foundgine.Providers.Aot.Generator / foundgine-aot-generator)','Manual semantic model builder'],{hint:'All compile to the same metadata registry; see docs/MAPPING.md.'}),
  T('basePackage','Namespace / base package',{req:1,hint:'C#: Acme.Rental. Java: com.acme.rental. The samples use Foundgine.SupplyChain.Advanced and com.foundgine.samples.supplychain.advanced.'}),
  T('groupArtifact','Project coordinates',{hint:'NuGet package id and/or Maven groupId : artifactId. Foundgine itself ships as io.github.cristianbarragan on Maven.'}),
  T('foundgineVersion','Foundgine version',{def:'3.4.0'}),
  T('port','HTTP port',{def:'4783',hint:'The Java sample serves /mcp and /intent on 4783.'}),
]},
{id:'actors',title:'2. Actors and questions',intro:'Who asks, and what an agent should be able to answer. These drive the example intents and scenario tests.',items:[
  L('actors','Actors / callers','Actor',[T('name','Name',{req:1}),A('desc','Description and what they should be allowed to do')]),
  L('questions','Questions an agent must answer','Question',[T('q','Question in plain language',{req:1}),T('entities','Entities involved (if known)'),T('who','Which actor asks it')]),
  A('out','Explicitly out of scope'),
]},
{id:'entities',title:'3. Entities and fields',intro:'The semantic model: entity names, aliases, fields and storage. Aliases carry a weight 0-100 that feeds grounding (e.g. "Vendor:95, Seller:90"). Mark entities that are only semantic/planning concepts so they do not become tables by accident.',items:[
  L('entities','Entities','Entity',[
    T('name','Semantic name (PascalCase)',{req:1}),
    A('desc','Description'),
    T('aliases','Aliases with weights',{hint:'Vendor:95, Seller:90'}),
    S('storage','Storage',['Persisted in a table','Semantic-only (planning concept, no table)','Event (timestamped, append-only)']),
    T('table','Table name (if persisted)',{hint:'Hand-written SQL names, e.g. warehouses, inventory_lots'}),
    T('key','Key field and type',{hint:'e.g. id int, or id uuid'}),
    C('tenantScoped','Tenant-scoped (has a tenant field)'),
    T('tenantField','Tenant field name',{def:'tenantId'}),
    T('eventTs','Event timestamp field (events only)'),
    T('dimensions','Dimensions',{hint:'Groupable fields and their dimension names, e.g. country as "country", category as "category"'}),
    L('fields','Fields','Field',[
      T('name','Field name',{req:1}),
      S('type','Type',['String','int','long','BigDecimal','boolean','LocalDate','Instant','UUID','enum','other']),
      T('column','Column name',{hint:'if different from field'}),
      T('sqlType','SQL type / length / precision',{hint:'varchar(120), numeric(18,2)'}),
      C('required','Required / NOT NULL'),
      C('writable','Writable via mutation'),
      C('sensitive','Sensitive (restrict by role)'),
      T('constraint','Constraint / check',{hint:'range >= 0, unique, enum values'}),
      T('fAliases','Field aliases with weights',{hint:'State:85'}),
    ]),
  ]),
]},
{id:'rels',title:'4. Relationships',intro:'How entities connect. The semantic name is what intents use; the foreign key is the physical join.',items:[
  L('rels','Relationships','Relationship',[
    T('from','From entity',{req:1}),T('name','Relationship name (as used in intents)',{req:1}),T('to','To entity',{req:1}),
    S('card','Cardinality',['One','Many']),
    T('fk','Foreign key',{hint:'e.g. InventoryLot.warehouseId references Warehouse.id'}),
    T('inverse','Inverse relationship name'),
    T('rAliases','Aliases with weights',{hint:'vendor:85'}),
    S('follow','Who may follow it',['Anyone who can read both ends','Same tenant only','Role-restricted (see section 7)','Custom rule']),
    A('notes','Notes',{hint:'Self-referencing? Recursive traversal (BOM, hierarchy)? Multi-hop paths (Product > Line > Order > Shipment)?'}),
  ]),
]},
{id:'persist',title:'5. Persistence',intro:'Physical storage. The Foundgine schema generator does not emit foreign keys, CHECKs, defaults, identity or extensions yet, so these stay in reviewed hand-written SQL.',items:[
  S('db','Database',['PostgreSQL 16 / 17','PostgreSQL (other version)','In-memory only (limited provider, tests and demos)','Other relational database via the SQL provider']),
  S('queryProvider','Primary query/mutation provider',['SQL provider (compiled, parameterised SQL; most control)','EF Core (.NET) over the shared ORM core','JPA (Java) over the shared ORM core','In-memory'],{req:1,hint:'Detail the full provider set in section 10.'}),
  S('ormPath','Mixed approach',['One provider only','SQL provider + EF Core / JPA for selected entities','EF Core / JPA for all persisted entities']),
  T('ormEntities','ORM-mapped entities (if any)',{hint:'.NET: the Roslyn ORM map generator writes typed maps. Java: JPA currently covers selected entities.'}),
  S('migr','Migrations',['Hand-written SQL baseline (Flyway on Java; SQL scripts on .NET)','Generated from model (foundgine-migrations)','EF Core migrations (.NET)','None, schema.sql only']),
  S('ids','ID strategy',['Integer keys, identity on transactional tables','UUID keys','Caller-supplied keys']),
  C('docker','Include docker-compose for PostgreSQL and any services selected in section 10',{def:true}),
  A('tenants','Tenants in sample data',{hint:'e.g. tenant-a, tenant-b (at least two, to prove isolation).'}),
  A('seed','Seed data notes',{hint:'Roughly how many rows per entity, any specific records the scenarios need.'}),
]},
{id:'semantic',title:'6. Semantic layer and grounding',intro:'How loose language becomes the right entity or field. Retrieval is candidate discovery only; it never authorizes.',items:[
  S('naming','Field naming',['PASCAL_CASE (sample default)','Other (explain in notes)']),
  M('retrieval','Candidate retrieval for ambiguous names/values',['exact','pg_trgm fuzzy','native full text','pg_search BM25','pgvector similarity','Elasticsearch lexical','graph similarity (Apache AGE)','alias weights only']),
  S('ambig','When ambiguity is unresolved',['Return REQUIRES_CLARIFICATION with candidates','Reject','AI decides']),
  T('budget','Grounding budget / deadline',{hint:'e.g. max 10 candidates, 250 ms retrieval deadline'}),
  L('search','Special candidate-search tools (optional)','Tool',[T('name','Tool name',{hint:'find_top_supplier_overdue_orders'}),A('desc','What it ranks and the outcomes it returns (resolved / clarification_needed / not_found)')]),
  A('glossary','Domain glossary and synonyms',{hint:'Business words people actually use for your entities and fields.'}),
]},
{id:'authz',title:'7. Authorization',intro:'Identity and authority are host-owned. The AI and the intent body never supply tenant or role. Policy decides what is visible; denied fields are pruned from results rather than erroring.',items:[
  S('tenancy','Tenancy model',['Tenant field on each tenant-scoped entity','Tenant inherited via parent entity','Single tenant']),
  S('identity','Where caller identity comes from',['Demo headers X-Foundgine-Tenant / X-Foundgine-Role (sample only)','OIDC / JWT','mTLS','Keycloak extension (identity + signed warrants)','ASP.NET authenticated principal (.NET)','Other']),
  L('roles','Roles','Role',[T('name','Role (UPPER_SNAKE)',{req:1}),A('read','Entities it can read'),A('denied','Entities / fields / relationships it cannot see'),A('write','What it can write')]),
  L('rules','Rules','Rule',[
    S('kind','Rule type',['Entity read','Entity write','Field read','Field write','Relationship read','Row predicate (e.g. tenant)','Named operation']),
    T('target','Target',{hint:'Supplier.RiskScore, Supplier, Warehouse.inventory'}),
    T('who','Allowed roles'),
    T('cond','Condition / notes',{hint:'tenantId equals caller tenant; requires claims reason + change_ticket'}),
  ]),
  L('claims','Client claims (narrowing only)','Claim',[T('key','Claim key',{hint:'warehouse, maxRows, readOnly, expiry'}),T('meaning','How it narrows access')]),
  T('reserved','Reserved/hostile claim keys to reject',{def:'role, isAdmin, tenant, tenantId'}),
]},
{id:'intent',title:'8. Canonical intent endpoint',intro:'One generic endpoint that takes a provider-neutral intent in the semantic vocabulary (rootEntity, selections, filter, order, limit, offset, after). Not one endpoint per query.',items:[
  M('paths','Endpoints',['POST /intent (execute)','POST /intent/explain (dry run: resolution, prunedFields, plan)','GET /intent/capabilities (what may this caller do)','MCP tool foundgine_intent','MCP tools foundgine_query / foundgine_mutation']),
  S('server','HTTP server',['ASP.NET Core minimal API (.NET sample)','JDK HttpServer, dependency-free (Java sample)','Spring MVC (Foundgine Spring auto-config)','Other']),
  T('limits','Limits',{def:'body 64 KiB, timeout 504, max rows per request: ',hint:'Error shape: {"error":{"type","message"}} with invalid_intent 400, forbidden 403, payload_too_large 413, method_not_allowed 405, timeout 504.'}),
  A('rootEntities','Entities exposed as intent roots',{hint:'Leave blank for all entities the policy allows.'}),
  L('examples','Example intents that must work','Intent',[
    T('title','What it demonstrates',{req:1}),
    T('role','Caller role'),T('tenant','Caller tenant'),
    A('json','Intent JSON',{code:1,hint:'{"rootEntity":"Supplier","selections":[{"field":"Name"},{"field":"RiskScore"}]}'}),
    A('expect','Expected result per role'),
  ]),
]},
{id:'mut',title:'9. Mutations (commands)',intro:'High-assurance writes such as place_order / cancel_order. Each is a command entity routed through the mutation engine and a domain service that is the final enforcement point.',items:[
  L('muts','Commands','Command',[
    T('name','Command name',{req:1,hint:'place_order'}),
    A('desc','What it does'),
    A('input','Input fields and types'),
    A('actor','Actor / tenant / ownership rules'),
    A('invariants','Invariants',{hint:'positive quantities, availability across authorized warehouses, status transitions allowed'}),
    S('idem','Idempotency',['Idempotency key with request fingerprint, replay returns original','None']),
    S('lock','Concurrency',['pg_advisory_xact_lock on idempotency key + row locks','Row locks only','Optimistic version column','AI decides']),
    T('server','Server-side calculations',{hint:'price, totals, allocation'}),
    A('effects','Tables / rows changed atomically'),
    T('backing','Backing',{def:'In-memory service for Main + PostgreSQL store'}),
  ]),
  S('approval','Approval flow for writes',['None','Dry run, then approve, then execute approved (foundgine_mutation_dry_run / _approve / _execute_approved)','Human approval through a Temporal workflow','Agent-framework approval (MAF / LangChain4j)']),
  C('readOnlyOk','No mutations needed (read-only app)'),
]},
{id:'adapters',title:'10. Providers and extensions',intro:'Everything optional that plugs into the core. A provider does the real work against storage or infrastructure; an extension connects a framework or service. Tick only what you need; the AI wires each one through the documented seams and keeps authority host-owned.',items:[
  M('exec','Query and mutation providers',['SQL provider (SqlCompiler / SqlExecutionProvider)','SQL mutations incl. PostgreSQL batched','In-memory provider','ORM core (shared, ORM-neutral)','EF Core provider (.NET)','JPA provider (Java, partial)'],{hint:'Pick the one primary provider in section 5; list any additional ones here.'}),
  M('retr','Candidate retrieval providers (grounding)',['PostgreSQL retrieval: pg_trgm + native full text','PostgreSQL pg_search (BM25)','PostgreSQL + Apache AGE graph similarity','PostgreSQL + pgvector','Elasticsearch lexical candidates']),
  M('durable','Durability and delivery',['PostgreSQL event store','PostgreSQL outbox + dispatcher','Gateway (provider registry, negotiation, approvals, HTTP/SSE routes, live updates)','Redis / Valkey / Garnet stores (warrant replay + revocation, MCP sessions)']),
  M('ai','AI and agents',['MCP server (tools, sessions, SSE, approval tokens)','MCP agent client','Microsoft.Extensions.AI toolset (.NET)','Microsoft Agent Framework (.NET, approval-gated writes)','LangChain4j tools (Java)']),
  M('ext','Extensions',['GraphQL (Hot Chocolate on .NET, GraphQL Java on Java) with secure execution','Keycloak identity and warrants (dev realm included)','Temporal durable workflows (retries, timers, approvals, compensation)']),
  M('host','Hosting and control',['ASP.NET Core hosting (.NET)','Spring Boot auto-configuration (foundgine.mcp.*, JPA FoundgineOrm)','JDK HttpServer (dependency-free)','Runtime control plane (tool registry and governance, approvals, audit, risk scoring, key rotation)']),
  M('build','Build-time generators and tooling',['AOT metadata generator','ORM map generator (.NET Roslyn)','foundgine-migrations (schema/migration source, Java)','Conformance vectors from src/conformance']),
  M('ops','Evidence and operations',['Execution evidence and receipts on every result','Audit log','Diagnostics and health endpoints','Live events (SSE)','Benchmark project like the Advanced sample']),
  M('sdk','Client SDKs and demos',['MCP client demo (raw HTTP)','TypeScript SDK example','Python SDK example','WebAssembly SDK example','Main / in-memory smoke test']),
  L('provCfg','Per-provider configuration','Provider',[
    T('name','Provider or extension',{req:1,hint:'e.g. Elasticsearch, Redis, Keycloak, Temporal'}),
    T('conn','Connection / endpoint / index / realm / task queue'),
    A('notes','Behaviour, limits and what it must prove in tests'),
  ]),
  A('notes','Other integrations or constraints'),
]},
{id:'tests',title:'11. Scenarios and tests',intro:'The sample proves its claims with adversarial tests, not only happy paths.',items:[
  L('scen','Scenarios','Scenario',[T('name','Name',{req:1}),A('desc','Setup and what must be true'),A('expect','Expected outcome')]),
  M('sec','Security / invariant tests to generate',['Tenant isolation: same intent, other tenant returns nothing','Same intent, different role: sensitive fields pruned not errored','Claims can only narrow, never expand access','Reserved claim keys (role, isAdmin) rejected','Identity-shaped property in intent body rejected','Unknown entity/field/relationship names rejected','Payload size limit and timeout','Idempotency replay returns original result','Concurrent replay of same key serializes','Recursive traversal with cycle detection','Alias grounding and REQUIRES_CLARIFICATION cases','Schema drift test vs SQL baseline']),
  A('accept','Acceptance criteria',{hint:'What makes you say "this works"?'}),
]},
{id:'gen',title:'12. Generation rules',intro:'How the AI should behave while generating.',items:[
  M('layout','Packages / files to generate',['model (fluent semantic + persistence + entity maps + connections)','domain (annotation-free records)','semantics','authorization (+ claims)','mutation','postgres','ORM mapping (EF Core / JPA)','intent (service, caller, endpoint)','mcp','providers and extensions wiring (section 10)','scenarios','data fixture','migrations / schema.sql / seed','tests','README (how to run)','Tutorial (file-by-file why)','Schema audit doc','docker-compose for every selected service'],{req:0}),
  S('blank','For blanks I left empty',['Choose sensible defaults and list them at the end','Ask me before generating','Insert TODO markers in the code']),
  C('verify','Compile and run the tests (dotnet test / mvn test) before declaring done; state plainly anything unverified',{def:true}),
  C('zip','Return the whole project as a zip with all files',{def:true}),
  A('extra','Extra rules or style constraints'),
]},
];

  function mount(host) {
    if (!host || host.__fsfMounted) return;
    host.__fsfMounted = true;
    var root = host.attachShadow({ mode: "open" });
    root.innerHTML = "<style>" + CSS + "</style>" + HTML;
    var box = root.getElementById("fsf");
    if (window.ResizeObserver) {
      new ResizeObserver(function (e) {
        box.classList.toggle("narrow", e[0].contentRect.width < 820);
      }).observe(box);
    }
// ---------- state ----------
let data={};
const LS='foundgine-form-v1';
function load(){try{const s=localStorage.getItem(LS);if(s)data=JSON.parse(s)}catch(e){}}
function save(){try{localStorage.setItem(LS,JSON.stringify(data))}catch(e){}}
function ensure(obj,items){items.forEach(it=>{if(it.def!==undefined&&obj[it.key]===undefined)obj[it.key]=it.def})}

// ---------- rendering ----------
function el(tag,attrs={},...kids){const e=document.createElement(tag);for(const k in attrs){if(k==='class')e.className=attrs[k];else if(k==='html')e.innerHTML=attrs[k];else e.setAttribute(k,attrs[k])}kids.forEach(k=>k&&e.append(k));return e}
function renderItems(items,obj,host,onchg){
  items.forEach(it=>{
    if(it.t==='list'){host.append(renderList(it,obj,onchg));return}
    const f=el('div',{class:'f'+(it.t==='area'||it.t==='multi'||it.t==='check'?'':'')});
    const lab=el('label',{class:it.req?'req':''});lab.textContent=it.label;
    if(it.t!=='check')f.append(lab);
    if(it.hint)f.append(el('div',{class:'h'},it.hint));
    let inp;
    if(it.t==='text'){inp=el('input',{type:'text'});inp.value=obj[it.key]??'';inp.oninput=()=>{obj[it.key]=inp.value;onchg()}}
    else if(it.t==='area'){inp=el('textarea',{class:it.code?'code':''});inp.value=obj[it.key]??'';inp.oninput=()=>{obj[it.key]=inp.value;onchg()}}
    else if(it.t==='select'){inp=el('select');const o0=el('option',{value:''});o0.textContent='- choose -';inp.append(o0);it.options.forEach(o=>{const op=el('option',{value:o});op.textContent=o;inp.append(op)});inp.value=obj[it.key]??'';inp.onchange=()=>{obj[it.key]=inp.value;onchg()}}
    else if(it.t==='multi'){inp=el('div',{class:'chk'});const cur=obj[it.key]=obj[it.key]||[];it.options.forEach(o=>{const cb=el('input',{type:'checkbox'});cb.checked=cur.includes(o);cb.onchange=()=>{const a=obj[it.key];if(cb.checked){if(!a.includes(o))a.push(o)}else obj[it.key]=a.filter(x=>x!==o);onchg()};const l=el('label',{});l.append(cb,document.createTextNode(o));inp.append(l)})}
    else if(it.t==='check'){const cb=el('input',{type:'checkbox'});cb.checked=!!obj[it.key];cb.onchange=()=>{obj[it.key]=cb.checked;onchg()};inp=el('div',{class:'chk'});const l=el('label',{});l.append(cb,document.createTextNode(it.label));inp.append(l)}
    f.append(inp);host.append(f);
  });
}
function renderList(it,obj,onchg){
  const wrap=el('div',{class:'f'});
  wrap.append(Object.assign(el('label',{}),{textContent:it.label}));
  const box=el('div',{});wrap.append(box);
  const arr=obj[it.key]=obj[it.key]||[];
  function draw(){
    box.innerHTML='';
    arr.forEach((row,i)=>{
      ensure(row,it.fields);
      const card=el('div',{class:'card'});
      const nm=row[it.fields[0].key];
      const hd=el('div',{class:'hd'});
      const b=el('b',{});b.textContent=it.item+' '+(i+1)+(nm?': '+nm:'');
      const rm=el('button',{class:'x',type:'button'});rm.textContent='Remove';rm.onclick=()=>{arr.splice(i,1);draw();onchg()};
      hd.append(b,rm);card.append(hd);
      const g=el('div',{class:'grid'});
      const simple=it.fields.filter(x=>x.t!=='list'),lists=it.fields.filter(x=>x.t==='list');
      // wrap areas/multi/lists as wide
      simple.forEach(fld=>{const h=el('div',{});renderItems([fld],row,h,()=>{onchg()});const node=h.firstChild;if(node){if(['area','multi'].includes(fld.t))node.classList.add('wide');g.append(node)}});
      card.append(g);
      lists.forEach(l=>renderItems([l],row,card,onchg));
      box.append(card);
    });
    const add=el('button',{type:'button'});add.textContent='+ Add '+it.item.toLowerCase();add.onclick=()=>{arr.push({});draw();onchg()};box.append(add);
  }
  draw();return wrap;
}

function build(){
  const main=root.getElementById('main'),nav=root.getElementById('nav');main.innerHTML='';nav.innerHTML='';
  SECTIONS.forEach(sec=>{
    data[sec.id]=data[sec.id]||{};ensure(data[sec.id],sec.items);
    const s=el('section',{id:sec.id});s.append(Object.assign(el('h2',{}),{textContent:sec.title}));
    if(sec.intro)s.append(Object.assign(el('p',{class:'intro'}),{textContent:sec.intro}));
    renderItems(sec.items,data[sec.id],s,changed);
    main.append(s);
    const a=el('a',{href:'#',id:'n-'+sec.id});a.onclick=ev=>{ev.preventDefault();const t=root.getElementById(sec.id);if(t)t.scrollIntoView({behavior:'smooth',block:'start'})};a.append(Object.assign(el('span',{}),{textContent:sec.title}),el('b',{},''));nav.append(a);
  });
  changed();
}
function reqMissing(){
  const miss=[];
  SECTIONS.forEach(sec=>{
    const d=data[sec.id]||{};
    const walk=(items,obj,path)=>items.forEach(it=>{
      if(it.t==='list'){const arr=obj[it.key]||[];arr.forEach((r,i)=>walk(it.fields,r,path+' > '+it.item+' '+(i+1)))}
      else if(it.req){const v=obj[it.key];if(v===undefined||v===''||(Array.isArray(v)&&!v.length))miss.push(path+' > '+it.label)}
    });
    walk(sec.items,d,sec.title);
  });
  return miss;
}
function reqTotals(){
  let tot=0,filled=0;
  SECTIONS.forEach(sec=>{
    const d=data[sec.id]||{};let st=0,sf=0;
    const walk=(items,obj)=>items.forEach(it=>{
      if(it.t==='list'){(obj[it.key]||[]).forEach(r=>walk(it.fields,r));}
      else if(it.req){st++;const v=obj[it.key];if(!(v===undefined||v===''||(Array.isArray(v)&&!v.length)))sf++}
    });
    // lists with required first field and no rows count as missing
    walk(sec.items,d);
    // add a "has any content" heuristic for sections without required fields
    let any=false;JSON.stringify(d,(k,v)=>{if(v&&v!==true&&!(Array.isArray(v)&&!v.length)&&typeof v!=='object')any=true;return v});
    const n=root.getElementById('n-'+sec.id);
    if(n){const done=st?sf===st:any;n.classList.toggle('done',done);n.lastChild.textContent=done?'✓':st?(sf+'/'+st):''}
    tot+=st;filled+=sf;
  });
  return [filled,tot];
}
function changed(){
  save();
  const [f,t]=reqTotals();
  const p=t?Math.round(100*f/t):0;
  root.getElementById('bar').style.width=p+'%';
  root.getElementById('pct').textContent=p+'% required filled';
}

// ---------- serialization ----------
function esc(v){return String(v).replace(/\r/g,'').trim()}
function mdItems(items,obj,lvl){
  let o='';
  items.forEach(it=>{
    const v=obj[it.key];
    if(it.t==='list'){
      const arr=v||[];if(!arr.length)return;
      o+='\n'+'#'.repeat(lvl)+' '+it.label+'\n';
      arr.forEach((r,i)=>{
        const title=r[it.fields[0].key]||('(unnamed '+it.item.toLowerCase()+')');
        o+='\n'+'#'.repeat(lvl+1)+' '+it.item+' '+(i+1)+': '+title+'\n';
        o+=mdItems(it.fields.slice(1),r,lvl+2);
        // first field already in title; but list first-field might also be a list? no
      });
    } else {
      if(it.t==='check'){if(v)o+='- '+it.label+': yes\n';return}
      if(v===undefined||v===''||(Array.isArray(v)&&!v.length))return;
      if(it.t==='multi'){o+='- '+it.label+':\n'+v.map(x=>'  - '+x).join('\n')+'\n'}
      else if(it.code){o+='- '+it.label+':\n```json\n'+esc(v)+'\n```\n'}
      else if(String(v).includes('\n')){o+='- '+it.label+':\n'+esc(v).split('\n').map(l=>'  '+l).join('\n')+'\n'}
      else o+='- '+it.label+': '+esc(v)+'\n';
    }
  });
  return o;
}
function specMd(){
  let o='';
  SECTIONS.forEach(sec=>{
    const body=mdItems(sec.items,data[sec.id]||{},3);
    o+='\n## '+sec.title+'\n'+(body.trim()?body:'- (nothing provided)\n');
  });
  return o;
}
const TPL_NET=[
'- .NET reference: src/csharp/samples/Foundgine.SupplyChain.Advanced (README.md, VERIFY-GATES.md, docs/00-Overview-And-Setup.md to 05-Adversarial-Security-Testing.md). The project lives under Semantic/: Domain, Semantics (model + Configuration), Authorization (+ Claims: ClaimSchema, IdentitySpoofingValidator, CrossFieldClaimValidator), Application/Scenarios, Intent (SupplyChainIntentService, IntentCaller, EvidencingProvider), Api/Mcp (Program.cs, IntentEndpoint.cs, IntentMcpTools.cs), Infrastructure/Data, McpClient, Tests (xUnit), Benchmarks.',
'- net10.0, ProjectReference or package references to Foundgine.Core and Foundgine.Providers; ModelContextProtocol.AspNetCore for MCP; Npgsql for PostgreSQL; actor resolved from the ASP.NET authenticated principal (demo: server-side FOUNDGINE_DEMO_ACTOR or the X-Foundgine-* headers, labelled DEMO ONLY).',
'- Verify with dotnet restore / build / test as in VERIFY-GATES.md.'
];
const TPL_JAVA=[
'- Java port: src/java/samples/foundgine-supply-chain-advanced (README.md, SupplyChain-Advanced-Tutorial.md, ADVANCED-SCHEMA-AUDIT.md). Package roles: model (SupplyChainFoundgineModel-style registrar + SemanticConfigurations + PersistenceConfigurations + EntityMaps + Connections + ModelConnections), domain (annotation-free records), semantics, authorization (+ claims), mutation, postgres, jpa, intent, mcp, scenarios, data, tests, resources (db/migration/V1__*.sql, seed, persistence.xml), docker-compose.yml, pom.xml joining the reactor (foundgine-core, foundgine-runtime, foundgine-providers, jackson-databind, postgresql driver).',
'- Java 27 and Maven. Note the Java sample is marked implemented-unverified until the Maven and PostgreSQL gates pass; say so for anything you cannot run.'
];
function preamble(){
  const lang=((data.project||{}).language)||'';
  const net=lang.indexOf('.NET')===0, jv=lang.indexOf('Java')===0, both=lang.indexOf('Both')===0;
  const L=[];
  L.push('You are generating a complete Foundgine application from the specification below. Foundgine is a semantic execution layer implemented in .NET 10 and Java 27 with the same architecture and vocabulary. The attached zip is the Foundgine repository; use it as the source of truth for APIs and conventions.','');
  L.push('## Language and template');
  if(net) L.push('Target: .NET 10 (C#).',...TPL_NET);
  else if(jv) L.push('Target: Java 27.',...TPL_JAVA,'- The .NET sample (src/csharp/samples/Foundgine.SupplyChain.Advanced) is the reference implementation; consult it when the Java behaviour is unclear.');
  else if(both) L.push('Target: BOTH. Build the .NET application first (it is the reference), then a Java port with identical entity/field/relationship names, aliases, roles, intents and tests. Use the shared vectors under src/conformance where they apply and flag any language-specific capability difference explicitly.','','### .NET',...TPL_NET,'','### Java',...TPL_JAVA);
  else L.push('The target language was not chosen. Ask which of .NET 10, Java 27 or both before generating.');
  L.push('','## Non-negotiable Foundgine rules',
'- Lifecycle: Caller > Intent > Semantic Model > Operation Graph > Retrieval > Resolution > Authorization > Plan Binding > Execution IR > Provider > Execution > Evidence.',
'- Retrieval is candidate discovery, never authorization. Unresolved ambiguity is rejected or returned as REQUIRES_CLARIFICATION, never guessed.',
'- Identity (tenant, role, audience) is host-owned. It is never part of the intent body; any identity-shaped property in an intent must be rejected as an unknown property. AI generates intent only.',
'- Client claims may only narrow access. Reject reserved/hostile keys (role, isAdmin, tenant...) before they reach policy.',
'- Fields the caller may not read are pruned from results (reported in explain as prunedFields), not turned into errors.',
'- The canonical intent endpoint is ONE generic endpoint (POST /intent, POST /intent/explain, GET /intent/capabilities, plus the foundgine_intent MCP tool) that takes the provider-neutral read intent {rootEntity, selections, filter, order, limit, offset, after} and runs it through a real Foundgine engine. It is not an endpoint per query.',
'- Domain types carry no Foundgine annotations when the fluent model style is chosen; semantic names, aliases (with weights), dimensions, storage and relationships live in the model configuration.',
'- Keep semantic/planning entities separate from persisted row types. Do not let semantic-only entities become tables.',
'- The schema generator does not emit foreign keys, CHECK constraints, defaults, identity or extensions: write reviewed SQL for those and add a schema drift test.',
'- Mutations go through the mutation engine; the domain service is the final enforcement point. Use idempotency keys with request fingerprints and server-side calculations.',
'- Do not claim anything is verified unless you actually ran the build and tests. Say plainly what was not run.',
'',
'## Providers and extensions',
'- Wire exactly the providers and extensions ticked in section 10 of the specification, through the documented seams (docs/wiki/providers.md, extensions.md, agent-framework-integrations.md, gateway-events.md, approvals-audit-governance.md, orm-providers.md, agents-mcp.md). Do not add unticked ones.',
'- A provider executes the already-authorized artifact and never reinterprets caller authority. An extension is an adapter, not a second semantic engine. Identity for Keycloak, warrants and agent frameworks is host-supplied, never model-supplied.',
'- Respect each provider\'s maturity notes in docs/wiki and the module READMEs (for example JPA is partial, Java PostgreSQL retrieval runtime is unverified, the MAF adapter is not build-verified) and report them instead of overclaiming.',
'- For every ticked provider or extension: add its configuration, a docker-compose service where one is needed, and at least one test or a documented verification step.',
'',
'## Process',
'1. Read the template sample(s) and the relevant core, runtime and provider code first.',
'2. Restate the model you will build (entities, relationships, roles, intents, providers) in a short summary and flag gaps or contradictions in the spec.',
'3. Generate in stages: model + domain + SQL, authorization, intent service and endpoint, mutations, providers and extensions, MCP, scenarios and tests, docs.',
'4. Build and test with the language\'s toolchain. Return the whole project as a zip with all files.',
'','# SPECIFICATION');
  return L.join('\n');
}
function prompt(){return preamble()+'\n'+specMd()+'\n\n# END OF SPECIFICATION\nFor any blank item, follow the rule in section 12. Start with step 1 and step 2, then continue.\n'}
function pruneEmpty(v){if(Array.isArray(v))return v.map(pruneEmpty).filter(x=>x!==undefined);if(v&&typeof v==='object'){const o={};for(const k in v){const p=pruneEmpty(v[k]);if(p!==undefined)o[k]=p}return Object.keys(o).length?o:undefined}if(v===''||v===false||v===undefined)return undefined;return v}
function json(){return JSON.stringify(pruneEmpty(data)||{},null,2)}

// ---------- UI wiring ----------
let mode='prompt';
const out=root.getElementById('out'),ta=root.getElementById('outText'),warn=root.getElementById('warn');
function show(m){
  mode=m;
  ['tPrompt','tJson','tImp'].forEach(id=>root.getElementById(id).classList.remove('on'));
  root.getElementById({prompt:'tPrompt',json:'tJson',imp:'tImp'}[m]).classList.add('on');
  warn.innerHTML='';
  if(m==='prompt'){ta.value=prompt();ta.readOnly=true;const miss=reqMissing();if(miss.length)warn.innerHTML='<div class="warnbox"><b>'+miss.length+' required item(s) still empty:</b><br>'+miss.slice(0,12).map(x=>x.replace(/</g,'&lt;')).join('<br>')+(miss.length>12?'<br>...':'')+'</div>'}
  else if(m==='json'){ta.value=json();ta.readOnly=true}
  else{ta.value='';ta.readOnly=false;ta.placeholder='Paste previously exported JSON here, then press Apply';warn.innerHTML='<div class="row"><button class="primary" id="bApply">Apply import</button></div>';root.getElementById('bApply').onclick=()=>{try{data=JSON.parse(ta.value);build();out.style.display='none'}catch(e){alert_('Invalid JSON: '+e.message)}}}
  out.style.display='block';
}
function alert_(m){root.getElementById('copyMsg').textContent=m}
root.getElementById('bGen').onclick=()=>show('prompt');
root.getElementById('bImport').onclick=()=>show('imp');
root.getElementById('tPrompt').onclick=()=>show('prompt');
root.getElementById('tJson').onclick=()=>show('json');
root.getElementById('tImp').onclick=()=>show('imp');
root.getElementById('bClose').onclick=()=>out.style.display='none';
root.getElementById('bCopy').onclick=async()=>{
  const msg=root.getElementById('copyMsg');
  try{await navigator.clipboard.writeText(ta.value);msg.textContent='Copied.'}
  catch(e){ta.removeAttribute('readonly');ta.focus();ta.select();msg.textContent='Select-all done; press Ctrl/Cmd+C.'}
};
let armed=false;
root.getElementById('bReset').onclick=function(){
  if(!armed){armed=true;this.textContent='Click again to clear';setTimeout(()=>{armed=false;this.textContent='Clear'},3000);return}
  armed=false;this.textContent='Clear';data={};build();
};
root.getElementById('bExample').onclick=()=>{data=EXAMPLE();build()};

function EXAMPLE(){return {
project:{name:'EquipmentRental',purpose:'Small equipment-rental business. Staff and agents ask which items are available, who has what out, and which rentals are overdue; customers can book and cancel.',language:'Java 27 (Maven)  - port: foundgine-supply-chain-advanced',shape:'New sample module inside the Foundgine reactor (like foundgine-supply-chain-advanced)',basePackage:'com.foundgine.samples.rental',groupArtifact:'io.github.cristianbarragan : foundgine-sample-rental',foundgineVersion:'3.4.0',port:'4790'},
actors:{actors:[{name:'CUSTOMER',desc:'Books and cancels own rentals; sees equipment catalogue.'},{name:'CLERK',desc:'Sees all rentals in own branch tenant.'},{name:'MANAGER',desc:'Sees everything incl. replacement cost.'}],questions:[{q:'Which rentals are overdue?',entities:'Rental, Customer',who:'CLERK'},{q:'What drill models are free this weekend?',entities:'Equipment',who:'CUSTOMER'}],out:'Payments, invoicing.'},
entities:{entities:[
{name:'Customer',storage:'Persisted in a table',table:'customers',key:'id int',tenantScoped:true,tenantField:'tenantId',aliases:'Client:90, Renter:85',fields:[{name:'Name',type:'String',sqlType:'varchar(120)',required:true},{name:'TenantId',type:'String',sqlType:'varchar(80)',required:true}]},
{name:'Equipment',storage:'Persisted in a table',table:'equipment',key:'id int',tenantScoped:true,aliases:'Tool:90, Gear:80',dimensions:'category as "category"',fields:[{name:'Name',type:'String',sqlType:'varchar(120)',required:true},{name:'Category',type:'String',sqlType:'varchar(80)'},{name:'DailyRate',type:'BigDecimal',sqlType:'numeric(18,2)',constraint:'>= 0'},{name:'ReplacementCost',type:'BigDecimal',sqlType:'numeric(18,2)',sensitive:true}]},
{name:'Rental',storage:'Persisted in a table',table:'rentals',key:'id int',tenantScoped:true,aliases:'Booking:90, Hire:85',fields:[{name:'CustomerId',type:'int',required:true},{name:'EquipmentId',type:'int',required:true},{name:'DueOn',type:'LocalDate',required:true,fAliases:'ReturnDate:90'},{name:'Status',type:'enum',constraint:'Booked, Out, Returned, Cancelled'}]}]},
rels:{rels:[{from:'Rental',name:'customer',to:'Customer',card:'One',fk:'Rental.customerId references Customer.id',follow:'Same tenant only'},{from:'Rental',name:'equipment',to:'Equipment',card:'One',fk:'Rental.equipmentId references Equipment.id'},{from:'Customer',name:'rentals',to:'Rental',card:'Many',inverse:'customer'}]},
persist:{db:'PostgreSQL 16 / 17',queryProvider:'SQL provider (compiled, parameterised SQL; most control)',migr:'Hand-written SQL baseline (Flyway on Java; SQL scripts on .NET)',ormPath:'One provider only',ids:'Integer keys, identity on transactional tables',docker:true,tenants:'tenant-a, tenant-b'},
semantic:{naming:'PASCAL_CASE (sample default)',retrieval:['exact','pg_trgm fuzzy','alias weights only'],ambig:'Return REQUIRES_CLARIFICATION with candidates'},
authz:{tenancy:'Tenant field on each tenant-scoped entity',identity:'Demo headers X-Foundgine-Tenant / X-Foundgine-Role (sample only)',roles:[{name:'CUSTOMER',read:'Equipment, own Rentals',denied:'Equipment.ReplacementCost, other customers'},{name:'CLERK',read:'All except cost fields',denied:'Equipment.ReplacementCost'},{name:'MANAGER',read:'All',write:'All'}],rules:[{kind:'Field read',target:'Equipment.ReplacementCost',who:'MANAGER'},{kind:'Row predicate (e.g. tenant)',target:'Customer, Rental',who:'all',cond:'tenantId equals caller tenant'}],claims:[{key:'branch',meaning:'Restricts reads to one branch'}],reserved:'role, isAdmin, tenant, tenantId'},
intent:{paths:['POST /intent (execute)','POST /intent/explain (dry run: resolution, prunedFields, plan)','GET /intent/capabilities (what may this caller do)','MCP tool foundgine_intent'],server:'JDK HttpServer, dependency-free (Java sample)',examples:[{title:'Same intent, two roles',role:'CLERK vs MANAGER',tenant:'tenant-a',json:'{"rootEntity":"Equipment","selections":[{"field":"Name"},{"field":"ReplacementCost"}]}',expect:'MANAGER sees ReplacementCost; CLERK gets it pruned, not an error.'}]},
mut:{muts:[{name:'book_rental',desc:'Create a booking for a customer.',input:'customerId, equipmentId, from, to, idempotencyKey',actor:'Customer may only book for self; tenant must match.',invariants:'Equipment not double-booked in the date range; to after from.',idem:'Idempotency key with request fingerprint, replay returns original',lock:'pg_advisory_xact_lock on idempotency key + row locks',server:'total price = daily rate x days'},{name:'cancel_rental',desc:'Cancel a booking not yet picked up.',invariants:'Status must be Booked.'}]},
adapters:{exec:['SQL provider (SqlCompiler / SqlExecutionProvider)','SQL mutations incl. PostgreSQL batched'],retr:['PostgreSQL retrieval: pg_trgm + native full text'],ai:['MCP server (tools, sessions, SSE, approval tokens)'],ops:['Execution evidence and receipts on every result','Diagnostics and health endpoints'],sdk:['MCP client demo (raw HTTP)','Main / in-memory smoke test']},
tests:{scen:[{name:'Overdue rentals',desc:'Rentals with DueOn before today and Status Out.',expect:'Clerk sees them in own tenant only.'}],sec:['Tenant isolation: same intent, other tenant returns nothing','Same intent, different role: sensitive fields pruned not errored','Reserved claim keys (role, isAdmin) rejected','Identity-shaped property in intent body rejected','Idempotency replay returns original result'],accept:'mvn test green; curl examples return expected per-role output.'},
gen:{layout:['model (fluent semantic + persistence + entity maps + connections)','domain (annotation-free records)','semantics','authorization (+ claims)','mutation','postgres','intent (service, caller, endpoint)','mcp','providers and extensions wiring (section 10)','scenarios','data fixture','migrations / schema.sql / seed','tests','README (how to run)'],blank:'Choose sensible defaults and list them at the end',verify:true,zip:true}
}}
    load();build();
  }

  function mountAll() {
    var list = document.querySelectorAll("#foundgine-spec-form,[data-foundgine-spec-form]");
    for (var i = 0; i < list.length; i++) mount(list[i]);
  }

  window.FoundgineSpecForm = { mount: mount, mountAll: mountAll };
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", mountAll);
  else mountAll();
})();

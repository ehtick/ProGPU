# Attribute Metadata and Symbol Convention Model

Status: normative design and inventory
Applies to: schema providers, framework profiles, binders, emitters, analyzers, editors, hot reload, and user extensions.

## Governing model

The compiler derives XAML meaning from four ordered evidence classes:

1. intrinsic MS-XAML schema data;
2. explicit attribute metadata mapped by a registered provider;
3. explicit framework projections for non-CLR or generated metadata;
4. opt-in symbol-shape conventions and declared fallbacks.

Each selected result retains the Roslyn symbol, `AttributeData` (including its canonical `TypedConstant` values) when applicable, provider ID and priority, semantic ID, extracted neutral value, and inheritance origin. Emitters consume the resulting bound descriptor and never rediscover metadata.

An attribute metadata name is not itself a compiler behavior. A profile maps it to a neutral semantic ID and declares valid targets, inheritance, value source, version/capability, and priority. Rules with incompatible equal priority are errors. Unknown attributes remain visible through the canonical symbol and are not silently promoted to compiler behavior.

## Standard .NET XAML Services and WPF inventory

The WPF profile MUST inventory and test at least:

- `AmbientAttribute`, `ContentPropertyAttribute`, `RuntimeNamePropertyAttribute`, `DictionaryKeyPropertyAttribute`, `NameScopePropertyAttribute`, `UidPropertyAttribute`, and `XmlLangPropertyAttribute`;
- `ConstructorArgumentAttribute`, `ContentWrapperAttribute`, `DependsOnAttribute`, `UsableDuringInitializationAttribute`, `TrimSurroundingWhitespaceAttribute`, and `WhitespaceSignificantCollectionAttribute`;
- `TypeConverterAttribute`, `ValueSerializerAttribute`, `MarkupExtensionReturnTypeAttribute`, `XamlSetMarkupExtensionAttribute`, and `XamlSetTypeConverterAttribute`;
- `XmlnsDefinitionAttribute`, `XmlnsPrefixAttribute`, `XmlnsCompatibleWithAttribute`, and root-namespace metadata;
- `XamlDeferLoadAttribute` on types/members, retaining both loader and content-type constants for deferred factory validation;
- attached-property browsing attributes, `StyleTypedPropertyAttribute`, `TemplatePartAttribute`, `TemplateVisualStateAttribute`, localization, and serialization hints.

Tooling-only metadata is retained and exposed but MUST NOT change runtime construction unless a separate semantic rule says so.

`XamlSetMarkupExtensionAttribute` and `XamlSetTypeConverterAttribute` are executable object-writer contracts, not descriptive strings. Their inherited handler names resolve once to canonical `XamlSetValueHandlerShapeInfo` descriptors. The provider that mapped the annotation semantic supplies that same family's event-args type; an unrelated higher-priority provider cannot replace it through a merged map. Roslyn validates the nearest static `void (object, TEventArgs)` method and retains malformed candidates for diagnostics. Accessibility is separate evidence: public/internal callbacks callable from the generated assembly can be referenced directly, while a valid private callback requires a profile-owned typed access bridge. During emission, the core supplies the exact descriptor and value evidence to a profile-owned interception contract. The profile implements its event-args, services, `Handled`, base-callback, access-bridge, and fallback protocol without reflection or handler-name rediscovery.

Legacy markup-extension receivers are a separate, explicitly enabled compatibility shape. A provider registers exact interface, callable, markup-extension parameter, and service-provider type metadata names; it may independently enable public duck-method recognition. `XamlMarkupExtensionReceiverShapeInfo` retains the exact identity and callable symbols, every named candidate, both non-string parameter symbols, and provider/error evidence. Interface identity wins over duck recognition, while a valid `XamlSetMarkupExtensionAttribute` callback wins over both. The neutral emitter constructs the extension once and hands the descriptor to a profile-owned structured lowering seam that owns service creation and fallback. `AcceptedMarkupExtensionExpressionTypeAttribute` is obsolete parser-ignored metadata and remains tooling-only.

`TypeConverterAttribute` and `ValueSerializerAttribute` occupy different directions. The converter is load-path text construction and publishes an exact constructor plus `ConvertFromInvariantString(string)` symbol. The value serializer is save-path metadata and cannot become the effective input text syntax. A provider-scoped serializer contract validates the declared base, context, public parameterless constructor, `CanConvertToString(object, context)`, and `ConvertToString(object, context)` methods. The type/member descriptor retains exact symbols and all candidates; an explicit null serializer is valid suppression evidence. Writer/editor code consumes the descriptor through structured Roslyn syntax.

`Windows.Foundation.Metadata.CreateFromStringAttribute` and registered equivalents are load-path text construction contracts. The named method string resolves once through Roslyn to either a static method on the attributed type or a qualified helper type. `XamlCreateFromStringShapeInfo` distinguishes this static invocation from converter-instance invocation while retaining target/factory/method/parameter/result symbols, candidates, provider evidence, and invalid state. A valid method is public, static, non-generic, accepts one by-value `String`, and returns a non-void value convertible to the attributed type. Member conversion metadata retains precedence over the value type. Binding carries the exact descriptor and raw text into IR; the emitter creates a structured invocation and result cast without reflection, attribute construction, or name rediscovery.

Whitespace and initialization attributes are executable schema policy, not catalog-only labels. `TrimSurroundingWhitespaceAttribute` and `WhitespaceSignificantCollectionAttribute` are inherited presence flags. `UsableDuringInitializationAttribute` is an inherited Boolean value for which a derived `false` must override a base `true`. All three become `XamlSchemaBooleanInfo` values with exact Roslyn annotation, provider, declaring symbol, and inheritance evidence. Whitespace normalization is delayed until binding so collection significance and child trim metadata are known; the schema-neutral infoset remains lossless and bound text retains its lexical spelling. For ordinary created children, valid usable-during-initialization evidence becomes an explicit top-down IR schedule: structured generation declares the child, publishes it through the selected parent operation, and then populates it. Other object kinds and explicit `false` retain bottom-up behavior; no name-based or reflection-based inference is authorized.

`ContentWrapperAttribute` is a repeatable inherited collection contract. Each declaration becomes a `XamlContentWrapperShapeInfo` containing its exact annotation/provider, wrapper type and constructor, wrapper content member, accepted content type, and validation error if malformed. The collection's exact `Add` item symbol constrains wrapper result types. Binding never wraps already assignable items; foreign content uses exact-then-most-specific selection and reports no-match or ambiguity rather than choosing declaration order. The synthesized wrapper is an ordinary typed bound/IR object, so emitters never inspect the attribute or invoke a wrapper dynamically.

`ConstructorArgumentAttribute` is save-path round-trip metadata. The attributed property maps to one same-typed named parameter on a public one-argument constructor and must remain publicly readable/writable. `XamlConstructorArgumentShapeInfo` retains exact annotation, constructor, parameter, candidates, and provider evidence. Load binding is unchanged; writer/editor code must explicitly opt into the parameterized representation and use the structured Roslyn constructor factory. This prevents a serialization hint from becoming an undocumented runtime construction heuristic.

`AmbientAttribute` is context-flow metadata on types, properties, and attached-property getters. A member is effectively ambient when it is directly annotated or its value type is ambient. Both forms retain exact source/reference Roslyn evidence; attached getter annotations are composed with setter metadata instead of being lost. The immutable ambient context graph indexes every bound node/member by stable identity, preserves type/member and owner evidence, uses the same canonical member ordering as lowering, makes an owner's ambient values available throughout its nested schema scope, and records deferred boundaries for later factory capture. Ambient dictionary members establish neutral lexical resource scopes even when their names are unknown to the framework profile. Non-dictionary ambient values stay canonical for service-context and save-path consumers and are never converted into resource semantics merely because they are ambient.

`XamlDeferLoadAttribute` is a paired loader/content contract on a type or member. Type-valued and string-valued constructors resolve through the same Roslyn compilation. `XamlDeferringLoaderShapeInfo` keeps declared names, exact loader/content/constructor/load/save/candidate symbols, provider evidence, and validation failures. The structural method-pair validation is intentionally duck typed against the public contract, while actual reader/service creation is a profile seam. A directly attributed member wins over its value type. Effective deferred members bind node streams, lower distinctly, capture ambient/deferred context, and expose structured load/save syntax without runtime discovery.

`MarkupExtensionBracketCharactersAttribute` is repeatable member-level parser policy. Every opening/closing `Char` pair retains complete attribute/provider evidence. Valid pairs flow through `XamlMarkupBracketPolicy` into the same bounded tokenizer/parser used by every framework; nested pairs protect internal commas and equals without changing quoted strings or nested extensions. The schema-neutral infoset retains only the root decoded markup value, and semantic binding resolves each extension/member through Roslyn before one-time re-projection, including nested extensions. Conflicting closers, malformed arguments, and reserved grammar characters remain canonical diagnostics. Pair maps are member-scoped—combining every project pair globally is not a conforming substitute.

`NameScopePropertyAttribute`, `XmlLangPropertyAttribute`, and `UidPropertyAttribute` produce exact aliased-member shapes. Direct aliases retain their `IPropertySymbol`; the namescope `(String, Type)` form retains its attached owner plus selected `Get*`/`Set*` methods. Storage metadata does not imply that the attributed type owns a scope. Ownership is modeled independently through `XamlNameScopeShapeInfo`: profiles register interface identities and may explicitly enable configurable register/unregister/find duck methods. Exact interface identity takes precedence and both forms retain selected Roslyn symbols, matching System.Xaml's distinction between `NameScopePropertyAttribute` and `XamlType.IsNameScope`.

`DependsOnAttribute` produces repeatable symbol-backed edges rather than ordering strings. Each edge must name a simple public instance property declared on the same CLR type; attached or qualified targets are rejected. The binder, ambient graph, construction IR, future serializer, and editor ordering services share the resulting stable topological order.

Avalonia `MarkupExtensionOptionAttribute` and `MarkupExtensionDefaultOptionAttribute` are separate member semantics. The keyed form retains its exact property, typed scalar value, and priority; the default form retains its exact property and explicit default role. Core schema validation rejects a property carrying both roles. A separately registered selector duck contract resolves exact public static Boolean methods with either an option parameter or a service-provider plus option parameter, preserves complete option/candidate collections and types, and diagnoses malformed shapes. Platform selection and branch trimming remain profile capabilities exposed through a structured Roslyn emission seam and must consume these descriptors rather than testing for Avalonia type names.

Avalonia `AvaloniaListAttribute` is an inherited class-only grammar annotation. The canonical descriptor consumes its `Separators` and `SplitOptions` named constants once, applies documented defaults, validates the collection and option bits, and exposes a reusable span-preserving splitter. Bound list items use the normal typed collection pipeline; no framework-name check or runtime attribute reflection is allowed downstream.

## WinUI inventory

The WinUI profile MUST inventory public metadata contracts including:

- `Microsoft.UI.Xaml.Markup.ContentPropertyAttribute` with inherited `Name` semantics;
- `MarkupExtensionReturnTypeAttribute` and the public markup-extension/service-provider contracts;
- type-level `FullXamlMetadataProviderAttribute` (on the complete metadata-provider runtime class), `XmlnsDefinition`, `IXamlType`, `IXamlMember`, `IXamlMetadataProvider`, component connector, generated binding/template component, and predicate metadata capabilities;
- `Microsoft.UI.Xaml.Data.BindableAttribute` and its distinction between runtime `Binding` source discoverability and compiled `x:Bind`;
- Windows metadata projection attributes only where they materially affect accessibility, activation, contracts, or generated construction.
- ProGPU.WinUI's `Microsoft.UI.Xaml.Markup.UsableDuringInitializationAttribute` extension maps to the same neutral inherited Boolean semantic as the standard .NET/Avalonia contracts. `ResourceDictionary` uses it to authorize attach-before-populate construction for theme partitions; the emitter does not infer this lifecycle from the type name.

WinRT metadata that is not represented as a CLR attribute is a typed framework projection, not a fake synthesized attribute.

`BindableAttribute` and `FullXamlMetadataProviderAttribute` are non-inherited marker evidence on their exact runtime classes. They are indexed as `XamlTypeMarkerInfo`; neither marker is executable and neither is propagated to derived CLR types.

Save/editor annotations are canonical schema evidence rather than ad-hoc designer reflection:

- `DefaultValueAttribute` retains either the exact value `TypedConstant` or the exact conversion type and text. It never initializes the property.
- `DesignerSerializationVisibilityAttribute` and WPF `DesignerSerializationOptionsAttribute` feed one `XamlMemberSerializationPolicy`. `Hidden` excludes, `Content` selects content form, and `SerializeAsAttribute` requests attribute form only when content/hidden does not override it.
- `BrowsableAttribute`, `EditorBrowsableAttribute`, and `DesignTimeVisibleAttribute` feed discovery policy only; they do not remove otherwise valid XAML members.
- WPF `LocalizabilityAttribute` retains neutral category/readability/modifiability enums plus the original constructor and named constants.

Inherited rules follow base types and overridden Roslyn property/event/method symbols. Attached-member save metadata is selected from the public getter and composed with setter annotations through the same precedence rules. Source and referenced-assembly symbols use the identical path; attributes are never instantiated.

The optional `DesignerSerializationMethods` symbol-shape family adds convention-based `ShouldSerialize<Property>()` and `Reset<Property>()` discovery. Prefixes and enablement are provider-owned, exact candidate/method symbols are retained, private source methods are permitted by the public designer contract, and invalid shapes or mixing with `DefaultValue` remain diagnostic evidence. The compiler exposes callables to writers and editors but never invokes them while binding.

These descriptors converge in `XamlSerializationPlanGraph`, which is created for every bound document. The graph uses stable object/member IDs and the same dependency order as lowering. It retains effective serializers, defaults, conditional/reset methods, and explicit member form without evaluating runtime values. Constructor-argument representation is a caller-selected alternative, never a load-path side effect. Structured Roslyn factories and reverse editing consume the plan; runtime enumeration and final lossless writing are later services over the same immutable contract.

## Avalonia inventory

The Avalonia profile MUST inventory and test at least:

- member-level `ContentAttribute`, `AmbientAttribute`, `DependsOnAttribute`, `TemplateContentAttribute`, and `ControlTemplateScopeAttribute`;
- `DataTypeAttribute`, `InheritDataTypeFromAttribute`, `InheritDataTypeFromItemsAttribute`, and `AssignBindingAttribute`;
- `MarkupExtensionDefaultOptionAttribute`, `MarkupExtensionOptionAttribute`, `AvaloniaListAttribute`, whitespace, initialization, and namespace attributes;
- name generation/build metadata and styled/direct/attached property registrations as framework projections;
- compiled versus reflection binding capabilities, including trimming/AOT diagnostics.

`AssignBindingAttribute` changes lowering: the binding object is assigned to the member instead of initiating the normal property binding operation.

## MAUI inventory

The MAUI profile MUST inventory and test at least:

- `ContentPropertyAttribute`, namespace definition/prefix metadata, XAML resource ID/file path metadata, and `XamlCompilationAttribute`;
- `AcceptEmptyServiceProviderAttribute` and `RequireServiceAttribute` on markup extensions;
- bindable-property fields/wrappers, templates, resources, compiled bindings, and markup-extension generic/non-generic interfaces as typed projections or shape rules.

Build controls normalize through `IXamlBuildMetadataResolver`, not a MAUI-specific emitter path. `XamlCompilationAttribute` retains its enum constant and assembly/module/class symbol; `XamlFilePathAttribute` retains the associated class; `XamlResourceIdAttribute` retains all three resource/path/type constructor values. Host item metadata wins, followed by class, module, and assembly evidence. WPF `RootNamespaceAttribute` participates in the same snapshot to root relative `x:Class` names. This compiler-wide build catalog is safe when the defining framework assembly is absent because Roslyn matches metadata names only against actual referenced symbols.

Service requirements participate in markup-extension activation and diagnostics; they are not documentation-only annotations.
The neutral descriptor retains each required service as an `ITypeSymbol`, the original array-valued `TypedConstant`, and whether an empty provider is permitted. A profile declares its available services and whether one of the two annotations is mandatory for that extension family. MAUI requires exactly one policy: every `IMarkupExtension` implementation declares its service dependencies with the single `RequireServiceAttribute(Type[])` or declares that no service is needed with `AcceptEmptyServiceProviderAttribute`. Unsupported services, missing declarations, and simultaneous require/accept-empty evidence are deterministic schema errors.

## Required symbol-shape rules

Every shape rule is opt-in and validates a complete contract.

| Shape | Required validation | Neutral result |
|---|---|---|
| Collection insertion | accessible instance method, configured name, one value parameter, unique applicable overload | collection add operation and item type |
| Dictionary insertion | accessible instance method, two parameters, unique applicable overload | dictionary add operation plus key/value types |
| Implicit dictionary key | registered exact metadata type, accessible writable instance member, no competing attribute alias | dictionary-key member descriptor |
| Resource member role | profile-declared member name, canonical accessible CLR member, compatible collection/dictionary/source shape | neutral lexical/merged/conditional/source role on `XamlMemberInfo` |
| Resource reference role | profile synthetic vocabulary or validated CLR type projection | neutral static/dynamic role on `XamlTypeInfo` |
| Add-child contract | registered interface identity and applicable object/text methods | content insertion strategy |
| Attached member | registered getter/setter prefixes, static form, target assignability, matching value/return types, unique overload | attachable member descriptor |
| Markup extension | registered suffix/base/interface, constructible activation shape, `ProvideValue` contract, declared service requirements | bound extension invocation |
| Object-writer interception | inherited handler annotation, registered event-args type, nearest static `void (object, TEventArgs)` callback, unique candidate, direct-access or typed-bridge classification | typed interception descriptor and profile assignment operation |
| Property-system wrapper | registered field/method identity, owner/value types, CLR wrapper consistency, read/write/bind operations | projected XAML member |
| Converter/serializer | registered attribute or interface/base shape, source/target compatibility, culture/service context | bound text conversion |
| Factory/deferred content | accessible registered method/delegate shape, return assignability, lifetime/namescope policy | construction or deferred-factory IR |
| Event | accessible event or explicitly registered routed-event pattern, delegate signature and handler compatibility | event subscription operation |
| Binding assignment | member annotation/projection and target API compatibility | assign binding object or initiate binding |

Generic inference, optional/`params` parameters, explicit interface implementations, extension methods, variance, nullability, accessibility, and ambiguous overloads MUST be handled deliberately. The default is rejection with a source diagnostic, never “first method wins.”

For attached accessor receiver overloads, exact Roslyn type identity wins. Otherwise the unique non-dominated compatible receiver wins: a parameter type is more specific when it implicitly converts to the competing parameter type and the reverse conversion does not exist. Equal/incomparable receivers or multiple value contracts remain ambiguous. This uses Roslyn conversions and retains the selected `IMethodSymbol`; declaration order never decides the result.

## Extension contract

Framework and user packages implement `IXamlSchemaMetadataProvider` and publish immutable attribute rules plus a `XamlSymbolShapePolicy`. The shape policy includes fully validated callable conventions and declarative projections for initialization-text types and parser-owned pseudo members. The production registry will add contract version, capabilities, dependency ordering, conflicts, and diagnostic ownership before third-party loading is declared stable.

Custom providers may introduce their own semantic IDs, but a custom lowering/validation capability must own each nonstandard semantic. Merely registering an attribute name cannot authorize arbitrary generated C#.

Framework dialect directives use the parallel `IXamlDialectDirectiveProvider` contract. A directive record carries its namespace, local name, allowed XML/XAML location, and eventually its neutral lowering semantic. The Roslyn type-system host indexes this vocabulary deterministically and exposes it through `IXamlDialectDirectiveResolver`; the neutral binder promotes only registered names to directive references. This keeps WinUI `x:Load` and `x:DeferLoadStrategy` out of the baseline intrinsic schema and ensures `x:Laod` remains an error.

## Parser-owned members and framework text syntax

A public XAML contract can expose semantic content without exposing a CLR member. A profile registers this as a `XamlPseudoMemberDefinition`, including canonical owner type, member name, neutral value type, `XamlMemberKind`, semantic capability, inheritance behavior, and provider-qualified provenance. Resolution searches the canonical type and permitted base types, then publishes a symbol-less `XamlMemberInfo`. The absence of `ISymbol` is intentional and MUST survive binding, IR, diagnostics, source maps, and tooling.

`DeferredContent` is the first such member kind. It denotes content owned by the XAML loader/compiler rather than an assignable property. It lowers to `SetDeferredContent` and requires a profile factory capability. A runtime object-model shim MUST NOT add a public property merely to make ordinary property emission succeed.

Types whose initialization-text grammar is supplied by a framework projection are listed by canonical metadata name in `ProfileTextSyntaxTypeMetadataNames`. The type system publishes `XamlTextSyntaxKind.Profile`; nullable wrappers delegate to their underlying type. The binder uses the same neutral initialization operation as other text-initialized values, while registered profile literal factories return Roslyn `ExpressionSyntax` directly.

## Getter-only attached collections

Some framework attached properties are intentionally exposed only as `GetX(target)` returning a mutable collection. When `InferGetterOnlyAttachedCollections` is enabled, the Roslyn provider accepts such a member only if the getter is public, static, unambiguous, takes exactly one compatible receiver, and returns a type with a validated collection or dictionary insertion shape. It publishes a retrievable attachable member with no setter. Binding uses retrieve-and-populate semantics and structured generation calls the canonical getter before adding children. Scalar getter-only shapes and incomplete or ambiguous accessor families remain errors.

An unqualified member first resolves through the ordinary instance-member path. If that fails, the Roslyn provider may retry the exact current type as the attached owner, enabling public WinRT XAML self-owner shorthand without weakening accessor validation. This is a fallback only: it cannot shadow a CLR property, accept a name-only match, or search unrelated owners.

## Provider composition and feature ownership

`XamlSymbolShapePolicy.DeclaredFeatures` records which convention families a provider intentionally supplies. Constructor defaults remain usable core behavior but are not declarations and therefore cannot erase or outrank another provider. Lists compose deterministically. Scalar groups and each keyed-map entry select the highest-priority declaring provider. Equivalent winners coalesce; incompatible winners publish `XamlSymbolShapeConflictInfo` with the feature, optional key, every provider ID/priority, and canonical values. `XamlSemanticBinder` reports `PGXAML2049` at the document root once per conflict, so provider IDs determine stable evidence order but never legitimize an arbitrary recovery choice.

Markup extensions require a stronger family boundary. Base-type, interface, and suffix identity are evaluated per provider. Once an identity provider wins, its callable names, service-provider parameter types, available services, and require-or-empty rule are used together. Policies from unrelated providers are not globally mixed for validation. This permits WinUI, MAUI, WPF, Avalonia, and user extension contracts to coexist in one Roslyn schema host while preserving exact provenance. Equivalent equal-priority contracts coalesce with every provider ID retained; incompatible equal-priority identity matches yield one invalid descriptor containing every competing provider ID.

## Current implementation boundary

Implemented now:

- provenance-bearing recognized annotation descriptors;
- type-level and member-level content discovery;
- inherited runtime-name and implicit dictionary-key aliases;
- profile-owned implicit dictionary-key conventions validated against Roslyn property symbols; WinUI `Style.TargetType` is the first configured convention and yields a typed `System.Type` key;
- profile-owned resource-member roles retained on canonical member descriptors; WinUI currently maps lexical resources, merged dictionaries, theme dictionaries, and source imports without teaching the neutral graph WinUI CLR names;
- profile-owned resource-reference roles retained on canonical type descriptors; semantic graphing does not infer static/dynamic behavior from markup-extension spelling;
- public one-/two-parameter insertion shapes;
- profile-declared add-child interface shapes, including structured interface-receiver casts for explicit implementations;
- target-assignable public static attached setters;
- profile-authorized getter-only attached collection inference with receiver, return-shape, insertion, and overload validation;
- configured markup-extension suffix resolution;
- emitter consumption of resolved runtime-name and insertion descriptors;
- assembly-symbol indexing for XML namespace definitions, preferred prefixes, and compatible namespace aliases, including source and referenced assemblies;
- general assembly-annotation indexing with canonical `AttributeData`, `TypedConstant`, provider, repeatability, and semantic evidence; current catalogs include root namespace, complete WinUI metadata, and MAUI XAML resource identity;
- assembly-qualified `clr-namespace:` lookup and compatibility-alias traversal with cycle protection;
- paired attached getter/setter validation for accessibility, static form, ref kinds, target assignability, matching value types, and overload uniqueness, with provider provenance;
- common/WinUI/WPF/Avalonia/MAUI public-contract catalogs;
- per-rule repeatability with deterministic preservation of distinct repeated values and provider priority evidence;
- canonical nullable Roslyn `TypedConstant` values alongside deterministic neutral display values, including type and array constants;
- retention and XAML-located diagnostics for incompatible single-valued evidence at equal provider priority and inheritance depth;
- `DependsOn` target/cycle validation and stable topological construction ordering while preserving unrelated source order;
- neutral namescope-member descriptors and attribute-driven `xml:lang`/`x:Uid` alias binding to canonical Roslyn members;
- type- and member-level `TypeConverterAttribute` resolution, converter-shape validation, bound/IR propagation, and typed structured invocation without runtime converter discovery;
- type- and member-level `ValueSerializerAttribute` resolution as a separate save-path descriptor, including exact callable/context/constructor symbols, candidate/provenance retention, explicit null suppression, malformed-shape diagnostics, metadata-reference coverage, IR retention, and structured writer syntax;
- canonical inherited `TrimSurroundingWhitespaceAttribute`, `WhitespaceSignificantCollectionAttribute`, and Boolean `UsableDuringInitializationAttribute` descriptors, including explicit derived `false`, source/reference symbol evidence, schema-aware element-content normalization, inherited `xml:space`, significant-collection preservation, adjacent-object trimming, and lossless original bound text;
- repeatable inherited `ContentWrapperAttribute` resolution with exact wrapper constructor/content-member/value-type evidence, item-type validation, direct-item bypass, exact/most-specific selection, explicit malformed/no-match/ambiguity diagnostics, metadata-reference coverage, bound/IR synthesis, and structured generated construction;
- member-level `ConstructorArgumentAttribute` save-path descriptors with exact one-argument constructor/parameter/candidate symbols, accessor and type validation, provider/reference evidence, load-path separation, malformed diagnostics, and structured writer object creation;
- inherited type/member/attached-getter `AmbientAttribute` descriptors with exact provider and Roslyn evidence, value-type-derived effective member ambience, deterministic accessor composition, stable-ID ambient context/deferred-boundary snapshots, shared construction ordering, and framework-name-free ambient dictionary resource scopes;
- type/member `XamlDeferLoadAttribute` descriptors covering CLR and string type identities, exact constructor/load/save/candidate symbols, duck-shape validation, member-over-type precedence, deferred bound/IR classification, ambient boundary capture, metadata-reference coverage, and structured load/save calls;
- repeatable member `MarkupExtensionBracketCharactersAttribute` descriptors with complete constructor identity, provider/reference evidence, conflict validation, bounded nested-pair parsing, lossless tokens, infoset option propagation, a direct member-to-parser policy adapter, and automatic exact-member semantic re-projection in generator/workspace pipelines;
- direct and attached `NameScopePropertyAttribute`, `XmlLangPropertyAttribute`, and `UidPropertyAttribute` descriptors retaining exact property/owner/accessor symbols, provider/reference evidence, directive projection, and invalid-shape diagnostics without conflating namescope storage with ownership;
- repeatable `DependsOnAttribute` descriptors retaining exact dependency-property symbols and provider/reference evidence, with one stable ordering service shared by binder, ambient context, lowering, and tooling;
- namescope ownership descriptors retaining exact interface or explicitly enabled duck-method identities and callable symbols, with semantic nested-scope validation independent from storage attributes;
- Avalonia markup-extension keyed/default option descriptors retaining exact typed constants, priority, property/provider/reference evidence, and malformed/conflicting metadata for later profile-owned structured branch lowering;
- Avalonia compiled-binding annotation descriptors retaining exact data-type properties, property/constructor-parameter scope inheritance constants, ancestor-item lookup types/properties, and property/method binding-assignment evidence; immutable stable-ID context snapshots carry bound data-type and nearest ancestor-item values, while a typed Roslyn profile seam owns binding-object publication;
- WinUI/WPF/Avalonia template-part, WinUI/WPF visual-state and style-typed-property, and WPF attached-property browse descriptors retaining exact declaring/property/getter/type symbols, inheritance, required/deep-child flags, provider/reference evidence, and malformed placement/duplicate diagnostics without changing runtime construction;
- CLR markup-extension recognition through profile-registered base/interface/suffix evidence plus a validated `ProvideValue` callable. The canonical descriptor retains exact identity/callable symbols, all candidates, service-provider type, provider provenance, and explicit error state. Context-aware overloads and uniquely more-specific service/interface types win; same-suffix/no-callable, invalid-service, and incomparable-callable cases are diagnosed. `MarkupExtensionReturnTypeAttribute` values remain canonical Roslyn type constants and are checked for assignment compatibility at the XAML target;
- explicit feature-family declarations and deterministic composition of independent symbol-shape providers; markup-extension callable/service validation is scoped to its winning identity provider, and incompatible equal-priority identity providers are diagnosed with complete provenance;
- canonical conflicts for attached-prefix, runtime-name, collection-inference, property-system, getter-only-attached, implicit-key, resource-role, and pseudo-member policy families, including equivalent/lower-priority recovery and `PGXAML2049` projection;
- provider-family-scoped object-writer event-args selection, preventing unrelated policies from changing an attributed callback contract;
- WinUI CLR extension construction reuses neutral construction/member IR and lowers through a structured profile invocation contract; the runtime supplies typed target-property, root-object, and read-only logical-base-URI services to `ProvideValue` without reflection;
- profile-declared initialization-text syntax for CLR-backed framework projections, including nullable unwrapping and structured literal emission;
- symbol-less pseudo content members and a distinct deferred-content schema/IR operation;
- profile-owned non-intrinsic directive recognition with location validation; WinUI `x:Load` and `x:DeferLoadStrategy` bind as directives while unknown `x:` names remain errors;
- deferred template members establish independent semantic namescopes for duplicate-name and `x:Reference` validation;
- profile-declared property-identifier suffix/type and setter-method shapes, with exact public static identifier, receiver method, parameter, return, accessibility, and overload validation; the selected Roslyn symbols and provider provenance are retained on the member descriptor and drive structured dynamic-resource assignment;
- XAML-located warning/error projection for standard `ObsoleteAttribute` and `ExperimentalAttribute` usage;
- tests for custom-provider provenance and representative shapes.

Still required before full conformance:

- semantic consumption of indexed assembly resource, root namespace, complete-metadata, and compilation-option annotations;
- semantic-specific neutral projections for composite/array attribute values beyond the retained Roslyn constants;
- unsupported-semantic diagnostics and conflicts between attribute, projection, and shape evidence;
- full property-wrapper projection/registration consistency, attached-property property-store lowering, value-serializer/create-from-string/general converter-method, WPF/Avalonia/MAUI markup-extension invocation and framework-specific service requirements, factory, routed-event, compiled binding-path analysis/runtime services, and remaining binding shapes;
- metadata-reference, inheritance/override, fuzz, performance, trimming, and AOT test matrices;
- binding and construction IR consumption of every semantic above.
- semantic lowering and runtime execution for dialect directives, including lazy factory publication and conditional `x:Load` state.

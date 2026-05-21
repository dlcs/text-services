# IIIF-CS Text Services

## Background

METS-ALTO is a standard for capturing the output of OCR and HTR processes:

https://www.loc.gov/standards/alto/

At it simplest, it's an XML file that records the position of words, lines and paragraphs in an image of text, typically from digitising a book. It therefore acts as a transcript, but also captures the exact layout of the text.

In this IIIF Manifest, you can see that each Canvas has a seeAlso property that links to a METS-ALTO XML document:

https://iiif.wellcomecollection.org/presentation/b21211024

The best way of telling that the resource linked to is an ALTO file is the profile property (here http://www.loc.gov/standards/alto/v3/alto.xsd) but failing that, the presence of "METS-ALTO" or "ALTO" in the label property is a clue. You will use this later to detect linked METS-ALTO files.

The `service` property of this Manifest links to a IIIF Search Service (version 1) at https://iiif.wellcomecollection.org/search/v1/b21211024, which in turn has an autocomplete service. Both of these are documented at https://iiif.io/api/search/1.0/.

This service is provided by a combination of two steps:

1. the one-off creation of a serialised binary object, on disk (or S3 etc), that acts as a map of all the text in the document, and
2. the runtime provision of a service that loads this object into memory on-demand and uses it to provide IIIF Search and Autocomplete API results.

We (my colleagues and I) have so far made two implementations of 1 and 2, both of which have small dependencies on their respective wider applications. You'll see that they have a lot of code in common; the second one was adapted from the first.

### Implementation 1: Wellcome

Solution root: C:\git\wellcomecollection\iiif-builder\src\Wellcome.Dds (most of this is not relevant to the project at hand).

C:\git\wellcomecollection\iiif-builder\src\Wellcome.Dds\Wellcome.Dds.Repositories\WordsAndPictures\AltoSearchTextProvider::GetSearchText builds a `Wellcome.Dds.WordsAndPictures.Text` object. There is extraneous code here that depends on Wellcome-specifics before it can get to the essential data - the width and height of the image, and the XElement that represents the ALTO file. This `Text` object is serialised to disk by BinaryObjectCache, which also manages the retrieval of objects from disk or S3 and holding them in MemoryCache.
At runtime, the ASP.NET controller `Wellcome.Dds.Server.Controllers.SearchController` has the actions `SearchV1` and `AutoCompleteV1` to provide the IIIF Search and Autocomplete services, that are consumed by IIIF viewers as the back end of "search within" features. While there is a hit on the additional initial load of a `Text` object from storage, we then keep the `Text` object in MemoryCache for a while, on the assumption that the user will make more than one query to it in a short space of time.

### Implementation 2: SL

Solution root: C:\git\digirati-co-uk\st-louis-fed\src\IIIFBuilder (again, most of this is not relevant to the project at hand).

This has the class `IIIFBuilder.Processor.Alto.Building.TextBuilder` to build the `Text` objects, and `IIIFBuilder.API.Features.Search.SearchController` for the runtime services. As you can see they have a lot in common with the Wellcome version, with slight local differences. For example, here the rescaling code is separated out to avoid multiple calls (see `AltoRescaler`).

### Points to note

 * The ALTO file alone may seem sufficient, because it records the image dimensions it was made from. But this image may have been scaled by the OCR software for optimum processing, and is not necessarily the same dimensions we want to use in our own Text map, which needs to correspond ultimately to the dimensions of a IIIF Canvas, so that word positions can be highlighted correctly.
 * As well as the text, we gather the bounding boxes of tables, illustrations and figures in the text. This is ultimately used to generate a single file for the whole manifest, identifying any images or tables in the text. You can see the end result in the Wellcome manifest linked earlier, which has an `annotations` property that links to https://iiif.wellcomecollection.org/annotations/v3/b21211024/images.
 * Since these implentations, there is a newer version of the IIIF Search API, which standardises the search responses to use annotations: https://iiif.io/api/search/2.0/. This is fundamentally similar, but the JSON shape of the responses is different. In the first pass at this we will implement IIIF Search 1, but then we'll come back and add support for Search 2, so keep that in mind.



## The Task

**Important - none of the following touches the two implementations above. We are going to build a _new_ .NET 10, C# Solution that is focused entirely on Text Services. But it is highly likely that it copies and re-uses the Text and associated classes, with their Protobuf serialisation, as this has proved effective in production for many years. The aim is to get a version recognisably similar to these two. Later on we can come back and see if it can be improved.** 

### Class Library

Fundamentally, the Text object is built from a sequence of URIs (file:, or http(s):) of ALTO files, and integer Canvas dimensions (width and height). So, we should be able to adapt the Wellcome/SL implementations into a new class library that accepts a data structure containing a sequence of dimensions and ALTO URIs and uses it to generate `Text` objects. In addition, for implementation of derived services later, for each entry in the sequence there is a string identifier. This class library probably doesn't do any storage, that's left to callers. How much I/O it does is a design decision, e.g., does the caller feed it `XElement` instances for each ALTO file, or does this class library fetch and read them itself? I think the former but am open to see where the design goes.


### API Text Building service

Then, an API service that callers can use to **create** Text objects from POSTed sequences, or posted references to URIs that will return such a sequence.

One such sequence is in fact a IIIF Manifest. Ignoring the fact that it already has a search service, the Manifest linked earlier contains Canvases (providing the w,h dimensions) and the links to METS-ALTO files (via each Canvas's `seeAlso` property - bearing in mind a Canvas may have many `seeAlso` properties and link to things other than METS-ALTO files). So as well as a "native" data structure sequence (see below), or a reference (URI) to one, the API should accept a reference to a IIIF Manifest, which internally it can load and reduce to our simple data structure for the purposes of generating a `Text` object. In the Manifest case, the `Wellcome.Dds.WordsAndPictures.Image::ImageIdentifier` in the Text object will be the Canvas `id` properties.

 * The text building service need only support IIIF Presentation version 3 Manifests, not older versions.
 * This `Text` object will later become the back end for providing search and complete services for the supplied IIIF Manifest - the caller will eventually be able to add the service URLs to their copy of the Manifest.
 * The incoming data structure/Manifest might be "sparse" in the sense that not every Canvas (not every width,height pair) has a corresponding ALTO file. This might be due to a mistake, or more likely efficiency - no point generating ALTO data for blank pages at digitisation/OCR time. And conceiveably, there might be no linked ALTO files at all and therefore no text to generate a `Text`. These are not errors.

This API service therefore accepts a sequence or a reference to a sequence, and begins a relatively long-running process of fetching each ALTO file in turn as it generates the `Text` object. Therefore this API is not synchronous; it accepts the POST as a job, and returns a reference to a job object that the caller can poll for completion. 

The POSTed job instruction might look like:

POST /textbuilder
```jsonc
{
    "id": "2/books/my-book", // a string that will be used later in service URLs; it is allowed to have / characters; it will be the last part of any later URLs we generate for services
    "sourceUri": "https://iiif.wellcomecollection.org/presentation/b21211024"
}
```

...where `sourceUri` is usually a URI that this API will fetch. The POSTed job object can instead have a `sourceData` property - an inline simple data structure - possibly like:

```json
[
    { 
        "id": "page/1",
        "width": 4000,
        "height": 6000,
        "text": "https://example.org/mets-alto/my-book/alto1.xml"
    },
    { 
        "id": "page/2",
        "width": 4004,
        "height": 6006,
        "text": "https://example.org/mets-alto/my-book/alto2.xml"
    }
]
```

This would be the data structure the Manifest is reduced to for Text building, with the Canvas IDs becoming `id` in this data structure. A POSTed job must have `sourceUri` or `sourceData` - it cannot have both. The `text` property is a URI that might be a "file" or "http(s)" or "s3" protocol URI (for example).

This job object created on the server might look something like:

```jsonc
{
    "id": "2/books/my-book", // unique identifier for this Text object, that can be used in URLs later - the job.id above
    "sourceUri": "https://example.org/iiif/my-book", // URI, or...
    "sourceData": null, // JSON array as above - must have one or the other
    "created": "2026-03-28T17:56:52Z", // DateTimeUtc
    "started": null,  // DateTimeUtc, may be null - job can accepted and queued
    "finished": null, // DateTimeUtc, null until complete
    "totalPages": 0, // total number of entried in the sequence
    "pagesCompleted": 0, // for polling, how many pages processed so far
    "totalWordCount": 0, // populated on completion, how many words
    "totalImageCount": 0,  // how many images/figures/tables were identified
    "errors": null,
    "searchV1": null, // when ready, the public URL of the search service
    "autocompleteV1": null, // when ready, the public URL of the autocomplete service,
    "searchV2": null, // same for IIIF Search V2
    "autocompletev2": null //  same for IIIF Search V2
    // and possible some additional fields for metrics and reporting
}
```

This API service will queue jobs to create text services (taking them serially, as soon as possible). It should return HTTP 201 Accepted with a Location header of (in this case) /textbuilder/2/books/my-book. Callers can GET this location to monitor the progress of their text-building hob

* The above implies that the API maintains state. It should do so in a simple PostgreSQL database, with a table for jobs (at least). I have a local PostgreSQL instance running which you can create a DB in for this work. The code should use EntityFramework and migrations. You'll need to ask me for admin credentials at the appropriate time. The table columns are not necessarily identical to the API object, for example, the search and autocomplete URIs are likely to be computed from the environment plus the `id` and not stored.

* The API should not depend on a particular storage implementation. It can have a filesystem implementation and an AWS S3 implementation initially, with more to follow. The Wellcome example has this but is probably far more complex than is needed just for this application (unless you think otherwise). Look at how SL does it, too.

* Where the supplied source is a IIIF Manifest, the service should *store a copy of the Manifest* as well as the Text object, which will be used later to host a version of the Manifest decorated with generated search services.


### The Search API

This is analagous to the `SearchController` implementations in the existing versions. I think it's a separate ASP.NET application (service), so there is no resource contention between building and public-facing search API delivery. Also, the building service may be turned off if not required.

The Search API takes the job ID as the tail of its path (not just the slug).

A job ID might be "2/books/my-book", and there will be search and autocomplete services at 

 - /search/v1/2/books/my-book
 - /autocomplete/v1/2/books/my-book

If the original source was a Manifest, there will also be a "decorated" IIIF Presentation 3 Manifest served at

 - /text-augmented/v3/2/books/my-book

This is identical to the original, except that:

 - its `id` is the fully qualified URL form of /text-augmented/2/books/my-book
 - it has a `service` property linking to the new search and autocomplete services

If it already has a `service` property we just append the new services to the end. The application doesn't really need to understand IIIF, it doesn't need a full-blown IIIF parser, it likely can just treat the Manifest as JSON and look for what it needs / patch its own version with the new services.

This Manifest can then be loaded into a search-supporting IIIF viewer, giving working search. Callers to the API can use this Manifest for testing, and likely they will take the search services from it and link to them in an updated version of their own original Manifest.


### Test suite

A full test suite using XUnit amd FluentAssertions should be built up to test the code as we go.

As well as unit tests, a Playwright test suite can do end to end tests.

You can obtain random Wellcome Manifests for testing from https://iiif.wellcomecollection.org/service/suggest-b-number?q=imfeelinglucky and inserting the returned b number into the template https://iiif.wellcomecollection.org/presentation/{b-number}. This will return a great mixture of content - some very small, some very big (100s of canvases), some without METS-ALTO links. You can use this service to build up a suitable library of test fixtures. Obviously, where they already have ALTO they already have Search too, but you can ignore the existing search services - unless perhaps to use them in a comparitive end-to-end test (do they yield the same results).


### Additional

 - Suggest any additional csproj division of the code base to favour maintainability and re-use.
 - While it only needs to support METS-ALTO as the text-bearing format initially, it should be designed in such a way that other formats such as hOCR and other text-segmentation formats can be supported in future, i.e., don't tie it to METS-ALTO.
 - There is more than one version of METS-ALTO but the basic principle is the same; we should be able to support all of them.


### Writing the code

I have an empty repo at C:\git\tomcrane\TextServices.

The .NET solution should live in src/

This document is in instructions/

I want you to build up the code as a series of well-described PRs to `main`, working on this local file system. You can make the PRs and I will read them (in addition to the more granular interaction in the terminal).

I want to create an additional web application alongside the services, for demonstrating how they are used.

One page is a super-simple "IIIF-Viewer" with search capability. A single-page Javascript web app, that takes the `iiif-content` query string parameter (and also provides an input box to supply a new one) and fetches it (assume it is a IIIF Presentation API v3 Manifest).

It displays one Canvas at a time - but is not a deep zoom viewer. It uses the static image that is the body of the first `painting` annotation on each Canvas, scaling it to fit a div that represents the Canvas. This div in turn should scale to fit on screen in the browser viewport. It has next and previous buttons to navigate through the pages.

If it detects a search service on the Manifest, it shows a UI for the user to type a query term in. These uses both the autocomplete service (if present) and the search service when the user submits the search form. It  then displays a list of results, with hit context if available. Clicking on a result navigates to that canvas and highlights the result on the div.

You can see something like this working at https://wellcomecollection.org/works/ahh9ugnd/items?canvas=7&query=advertido&shouldScrollToCanvas=true (see "Search within this item"). This one doesn't appear to use the autocomplete service. It also has a more sophisticated scrolling view of the images, but you don't have to implement that (unless you want to). The key thing is to demonstrate that the autocomplete and search services work, and are complete. As a demonstrator, the UI should deisplay somewhere that it is consuming the services, including their URLs, so that later for testing we know we are hitting the right service.

When selecting a search service to interact with, the application should pick the first one it finds - this will allow it to test the augemented manifests produced by our main TextServices builder.

Another page of the application allows the user to submit a Manifest for processing - to add a search service to it. Again, these will likely be Wellcome manifests with existing services, to which our builder will insert a new search service at position 0.

This page should be a simple dashboard for the TextBuilder, showing manifests it already has processed, and providing links for them to be loaded into the first page for viewing and testing.

When a user submits a Manifest for augmenting, this page can show a progress update by polling.

Put a link to the hangfire dash in as well.

A third page also takes a manifest on the query string in the iiif-content property, and is specifically for comparing search results when a Manifest has TWO search services - as ours will when they have decorated a Wellcome manifest.

This can have a very simplified display, submitting queries to both services and displaying the autocomplete and search results.
It could be a little clever and use the image services avaialble to make region image requests for the hit target rectangles, to compare the actual visual regions.


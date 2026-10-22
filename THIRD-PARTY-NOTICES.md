# Third-party notices

**CAD-URDF Maker** is a new implementation. Its packaged dependencies include:

- **Three.js 0.170.0**, copyright Three.js contributors, MIT license. The bundled license is `web/vendor/THREE-LICENSE.txt`. `OrbitControls.js` has one import-specifier adjustment for local offline loading.
- **Microsoft.Web.WebView2 1.0.2903.40**, Microsoft SDK license. The downloaded license is `vendor/webview2/LICENSE.txt`; the runtime package includes the SDK license as `licenses/WEBVIEW2-LICENSE.txt`.
- **TypeScript 5.7.2**, Microsoft and contributors, Apache-2.0 license. Build dependency only; license in `vendor/typescript/package/LICENSE.txt`.
- **SolidWorks interop assemblies**, Dassault Systèmes SolidWorks. The add-in requires a licensed SolidWorks 2024 installation. Deployment is for use with that installation and subject to the vendor's applicable SDK/API terms.

API documentation and the ROS SolidWorks exporter were consulted for API conventions, including product-of-inertia signs. No source files from that exporter are included.

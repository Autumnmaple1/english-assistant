# Third-party notices

The application uses the following open-source dependencies through NuGet: Microsoft Windows App SDK, Microsoft Windows SDK BuildTools, System.Drawing.Common, Tesseract (.NET wrapper), and JsonSchema.Net. Their license metadata is included in the restored packages. The Tesseract wrapper bundles native Tesseract and Leptonica libraries; consult the package's licenses when distributing a build.

## English OCR data

`data/tessdata/eng.traineddata` comes from [tesseract-ocr/tessdata_fast](https://github.com/tesseract-ocr/tessdata_fast), under the Apache License 2.0. Its license is downloaded alongside it as `data/tessdata/LICENSE.txt`.

SHA-256 for the verified model:

`7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2`

## WordNet

WordNet 3.0 is provided by Princeton University, downloaded through the [NLTK data repository](https://github.com/nltk/nltk_data). `data/wordnet.zip` contains WordNet's original license and documentation. This application reads the database locally and presents its definitions with a WordNet attribution label.

SHA-256 for the verified archive:

`cbda5ea6eef7f36a97a43d4a75f85e07fccbb4f23657d27b4ccbc93e2646ab59`

The original WordNet license is retained inside the archive. [License and commercial-use information](https://wordnet.princeton.edu/license-and-commercial-use).

## Cambridge

Cambridge Dictionary is an external website. The application links to it but does not bundle, scrape, or cache Cambridge definitions. Cambridge is not affiliated with this application.

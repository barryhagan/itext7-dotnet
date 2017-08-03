/*

This file is part of the iText (R) project.
Copyright (c) 1998-2017 iText Group NV
Authors: Bruno Lowagie, Paulo Soares, et al.

This program is free software; you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License version 3
as published by the Free Software Foundation with the addition of the
following permission added to Section 15 as permitted in Section 7(a):
FOR ANY PART OF THE COVERED WORK IN WHICH THE COPYRIGHT IS OWNED BY
ITEXT GROUP. ITEXT GROUP DISCLAIMS THE WARRANTY OF NON INFRINGEMENT
OF THIRD PARTY RIGHTS

This program is distributed in the hope that it will be useful, but
WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
or FITNESS FOR A PARTICULAR PURPOSE.
See the GNU Affero General Public License for more details.
You should have received a copy of the GNU Affero General Public License
along with this program; if not, see http://www.gnu.org/licenses or write to
the Free Software Foundation, Inc., 51 Franklin Street, Fifth Floor,
Boston, MA, 02110-1301 USA, or download the license from the following URL:
http://itextpdf.com/terms-of-use/

The interactive user interfaces in modified source and object code versions
of this program must display Appropriate Legal Notices, as required under
Section 5 of the GNU Affero General Public License.

In accordance with Section 7(b) of the GNU Affero General Public License,
a covered work must retain the producer line in every PDF that is created
or manipulated using iText.

You can be released from the requirements of the license by purchasing
a commercial license. Buying such a license is mandatory as soon as you
develop commercial activities involving the iText software without
disclosing the source code of your own applications.
These activities include: offering paid services to customers as an ASP,
serving PDFs on the fly in a web application, shipping iText with a closed
source product.

For more information, please contact iText Software Corp. at this
address: sales@itextpdf.com
*/
using System;
using System.Collections.Generic;
using System.Text;
using iText.IO.Log;
using iText.IO.Source;
using iText.IO.Util;
using iText.Kernel;
using iText.Kernel.Crypto;
using iText.Kernel.Events;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Log;
using iText.Kernel.Numbering;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Filespec;
using iText.Kernel.Pdf.Navigation;
using iText.Kernel.Pdf.Tagging;
using iText.Kernel.Pdf.Tagutils;
using iText.Kernel.XMP;
using iText.Kernel.XMP.Options;

namespace iText.Kernel.Pdf {
    /// <summary>Main enter point to work with PDF document.</summary>
    public class PdfDocument : IEventDispatcher, IDisposable {
        /// <summary>Currently active page.</summary>
        protected internal PdfPage currentPage = null;

        /// <summary>Default page size.</summary>
        /// <remarks>
        /// Default page size.
        /// New page by default will be created with this size.
        /// </remarks>
        protected internal PageSize defaultPageSize = PageSize.Default;

        [System.NonSerialized]
        protected internal EventDispatcher eventDispatcher = new EventDispatcher();

        /// <summary>PdfWriter associated with the document.</summary>
        /// <remarks>
        /// PdfWriter associated with the document.
        /// Not null if document opened either in writing or stamping mode.
        /// </remarks>
        protected internal PdfWriter writer = null;

        /// <summary>PdfReader associated with the document.</summary>
        /// <remarks>
        /// PdfReader associated with the document.
        /// Not null if document is opened either in reading or stamping mode.
        /// </remarks>
        protected internal PdfReader reader = null;

        /// <summary>XMP Metadata for the document.</summary>
        protected internal byte[] xmpMetadata = null;

        /// <summary>Document catalog.</summary>
        protected internal PdfCatalog catalog = null;

        /// <summary>Document trailed.</summary>
        protected internal PdfDictionary trailer = null;

        /// <summary>Document info.</summary>
        protected internal PdfDocumentInfo info = null;

        /// <summary>Document version.</summary>
        protected internal PdfVersion pdfVersion = PdfVersion.PDF_1_7;

        /// <summary>The ID entry that represents the initial identifier.</summary>
        [Obsolete]
        protected internal PdfString initialDocumentId;

        /// <summary>The ID entry that represents a change in a document.</summary>
        [Obsolete]
        protected internal PdfString modifiedDocumentId;

        /// <summary>The original second id when the document is read initially.</summary>
        private PdfString originalModifiedDocumentId;

        /// <summary>List of indirect objects used in the document.</summary>
        internal readonly PdfXrefTable xref = new PdfXrefTable();

        protected internal readonly StampingProperties properties;

        protected internal PdfStructTreeRoot structTreeRoot;

        protected internal int structParentIndex = -1;

        [Obsolete]
        protected internal bool userProperties;

        protected internal bool closeReader = true;

        protected internal bool closeWriter = true;

        protected internal bool isClosing = false;

        protected internal bool closed = false;

        /// <summary>flag determines whether to write unused objects to result document</summary>
        protected internal bool flushUnusedObjects = false;

        private IDictionary<PdfIndirectReference, PdfFont> documentFonts = new Dictionary<PdfIndirectReference, PdfFont
            >();

        private PdfFont defaultFont = null;

        [System.NonSerialized]
        protected internal TagStructureContext tagStructureContext;

        private static readonly AtomicLong lastDocumentId = new AtomicLong();

        private long documentId;

        /// <summary>Yet not copied link annotations from the other documents.</summary>
        /// <remarks>
        /// Yet not copied link annotations from the other documents.
        /// Key - page from the source document, which contains this annotation.
        /// Value - link annotation from the source document.
        /// </remarks>
        private LinkedDictionary<PdfPage, IList<PdfLinkAnnotation>> linkAnnotations = new LinkedDictionary<PdfPage
            , IList<PdfLinkAnnotation>>();

        /// <summary>Cache of already serialized objects from this document for smart mode.</summary>
        internal IDictionary<PdfIndirectReference, byte[]> serializedObjectsCache = new Dictionary<PdfIndirectReference
            , byte[]>();

        /// <summary>Open PDF document in reading mode.</summary>
        /// <param name="reader">PDF reader.</param>
        public PdfDocument(PdfReader reader) {
            if (reader == null) {
                throw new ArgumentNullException("reader");
            }
            documentId = lastDocumentId.IncrementAndGet();
            this.reader = reader;
            this.properties = new StampingProperties();
            // default values of the StampingProperties doesn't affect anything
            Open(null);
        }

        /// <summary>Open PDF document in writing mode.</summary>
        /// <remarks>
        /// Open PDF document in writing mode.
        /// Document has no pages when initialized.
        /// </remarks>
        /// <param name="writer">PDF writer</param>
        public PdfDocument(PdfWriter writer) {
            if (writer == null) {
                throw new ArgumentNullException("writer");
            }
            documentId = lastDocumentId.IncrementAndGet();
            this.writer = writer;
            this.properties = new StampingProperties();
            // default values of the StampingProperties doesn't affect anything
            Open(writer.properties.pdfVersion);
        }

        /// <summary>Opens PDF document in the stamping mode.</summary>
        /// <remarks>
        /// Opens PDF document in the stamping mode.
        /// <br/>
        /// </remarks>
        /// <param name="reader">PDF reader.</param>
        /// <param name="writer">PDF writer.</param>
        public PdfDocument(PdfReader reader, PdfWriter writer)
            : this(reader, writer, new StampingProperties()) {
        }

        /// <summary>Open PDF document in stamping mode.</summary>
        /// <param name="reader">PDF reader.</param>
        /// <param name="writer">PDF writer.</param>
        /// <param name="properties">properties of the stamping process</param>
        public PdfDocument(PdfReader reader, PdfWriter writer, StampingProperties properties) {
            if (reader == null) {
                throw new ArgumentNullException("reader");
            }
            if (writer == null) {
                throw new ArgumentNullException("writer");
            }
            documentId = lastDocumentId.IncrementAndGet();
            this.reader = reader;
            this.writer = writer;
            this.properties = properties;
            bool writerHasEncryption = writer.properties.IsStandardEncryptionUsed() || writer.properties.IsPublicKeyEncryptionUsed
                ();
            if (properties.appendMode && writerHasEncryption) {
                ILogger logger = LoggerFactory.GetLogger(typeof(iText.Kernel.Pdf.PdfDocument));
                logger.Warn(iText.IO.LogMessageConstant.WRITER_ENCRYPTION_IS_IGNORED_APPEND);
            }
            if (properties.preserveEncryption && writerHasEncryption) {
                ILogger logger = LoggerFactory.GetLogger(typeof(iText.Kernel.Pdf.PdfDocument));
                logger.Warn(iText.IO.LogMessageConstant.WRITER_ENCRYPTION_IS_IGNORED_PRESERVE);
            }
            Open(writer.properties.pdfVersion);
        }

        /// <summary>Use this method to set the XMP Metadata.</summary>
        /// <param name="xmpMetadata">The xmpMetadata to set.</param>
        protected internal virtual void SetXmpMetadata(byte[] xmpMetadata) {
            this.xmpMetadata = xmpMetadata;
        }

        /// <exception cref="iText.Kernel.XMP.XMPException"/>
        public virtual void SetXmpMetadata(XMPMeta xmpMeta, SerializeOptions serializeOptions) {
            SetXmpMetadata(XMPMetaFactory.SerializeToBuffer(xmpMeta, serializeOptions));
        }

        /// <exception cref="iText.Kernel.XMP.XMPException"/>
        public virtual void SetXmpMetadata(XMPMeta xmpMeta) {
            SerializeOptions serializeOptions = new SerializeOptions();
            serializeOptions.SetPadding(2000);
            SetXmpMetadata(xmpMeta, serializeOptions);
        }

        /// <summary>Gets XMPMetadata.</summary>
        public virtual byte[] GetXmpMetadata() {
            return GetXmpMetadata(false);
        }

        /// <summary>Gets XMPMetadata or create a new one.</summary>
        /// <param name="createNew">if true, create a new empty XMPMetadata if it did not present.</param>
        /// <returns>existed or newly created XMPMetadata byte array.</returns>
        public virtual byte[] GetXmpMetadata(bool createNew) {
            if (xmpMetadata == null && createNew) {
                XMPMeta xmpMeta = XMPMetaFactory.Create();
                xmpMeta.SetObjectName(XMPConst.TAG_XMPMETA);
                xmpMeta.SetObjectName("");
                AddCustomMetadataExtensions(xmpMeta);
                try {
                    xmpMeta.SetProperty(XMPConst.NS_DC, PdfConst.Format, "application/pdf");
                    xmpMeta.SetProperty(XMPConst.NS_PDF, PdfConst.Producer, iText.Kernel.Version.GetInstance().GetVersion());
                    SetXmpMetadata(xmpMeta);
                }
                catch (XMPException) {
                }
            }
            return xmpMetadata;
        }

        /// <summary>Gets PdfObject by object number.</summary>
        /// <param name="objNum">object number.</param>
        /// <returns>
        /// 
        /// <see cref="PdfObject"/>
        /// or
        /// <see langword="null"/>
        /// , if object not found.
        /// </returns>
        public virtual PdfObject GetPdfObject(int objNum) {
            CheckClosingStatus();
            PdfIndirectReference reference = xref.Get(objNum);
            if (reference == null) {
                return null;
            }
            else {
                return reference.GetRefersTo();
            }
        }

        /// <summary>Get number of indirect objects in the document.</summary>
        /// <returns>number of indirect objects.</returns>
        public virtual int GetNumberOfPdfObjects() {
            return xref.Size();
        }

        /// <summary>Gets the page by page number.</summary>
        /// <param name="pageNum">page number.</param>
        /// <returns>page by page number.</returns>
        public virtual PdfPage GetPage(int pageNum) {
            CheckClosingStatus();
            return catalog.GetPageTree().GetPage(pageNum);
        }

        /// <summary>
        /// Gets the
        /// <see cref="PdfPage"/>
        /// instance by
        /// <see cref="PdfDictionary"/>
        /// .
        /// </summary>
        /// <param name="pageDictionary">
        /// 
        /// <see cref="PdfDictionary"/>
        /// that present page.
        /// </param>
        /// <returns>
        /// page by
        /// <see cref="PdfDictionary"/>
        /// .
        /// </returns>
        public virtual PdfPage GetPage(PdfDictionary pageDictionary) {
            CheckClosingStatus();
            return catalog.GetPageTree().GetPage(pageDictionary);
        }

        /// <summary>Get the first page of the document.</summary>
        /// <returns>first page of the document.</returns>
        public virtual PdfPage GetFirstPage() {
            CheckClosingStatus();
            return GetPage(1);
        }

        /// <summary>Gets the last page of the document.</summary>
        /// <returns>last page.</returns>
        public virtual PdfPage GetLastPage() {
            return GetPage(GetNumberOfPages());
        }

        /// <summary>Creates and adds new page to the end of document.</summary>
        /// <returns>added page</returns>
        public virtual PdfPage AddNewPage() {
            return AddNewPage(GetDefaultPageSize());
        }

        /// <summary>Creates and adds new page with the specified page size.</summary>
        /// <param name="pageSize">page size of the new page</param>
        /// <returns>added page</returns>
        public virtual PdfPage AddNewPage(PageSize pageSize) {
            CheckClosingStatus();
            PdfPage page = new PdfPage(this, pageSize);
            CheckAndAddPage(page);
            DispatchEvent(new PdfDocumentEvent(PdfDocumentEvent.START_PAGE, page));
            DispatchEvent(new PdfDocumentEvent(PdfDocumentEvent.INSERT_PAGE, page));
            return page;
        }

        /// <summary>Creates and inserts new page to the document.</summary>
        /// <param name="index">position to addPage page to</param>
        /// <returns>inserted page</returns>
        /// <exception cref="iText.Kernel.PdfException">
        /// in case
        /// <c>page</c>
        /// is flushed
        /// </exception>
        public virtual PdfPage AddNewPage(int index) {
            return AddNewPage(index, GetDefaultPageSize());
        }

        /// <summary>Creates and inserts new page to the document.</summary>
        /// <param name="index">position to addPage page to</param>
        /// <param name="pageSize">page size of the new page</param>
        /// <returns>inserted page</returns>
        /// <exception cref="iText.Kernel.PdfException">
        /// in case
        /// <c>page</c>
        /// is flushed
        /// </exception>
        public virtual PdfPage AddNewPage(int index, PageSize pageSize) {
            CheckClosingStatus();
            PdfPage page = new PdfPage(this, pageSize);
            CheckAndAddPage(index, page);
            currentPage = page;
            DispatchEvent(new PdfDocumentEvent(PdfDocumentEvent.START_PAGE, page));
            DispatchEvent(new PdfDocumentEvent(PdfDocumentEvent.INSERT_PAGE, page));
            return currentPage;
        }

        /// <summary>Adds page to the end of document.</summary>
        /// <param name="page">page to add.</param>
        /// <returns>added page.</returns>
        /// <exception cref="iText.Kernel.PdfException">
        /// in case
        /// <paramref name="page"/>
        /// is flushed
        /// </exception>
        public virtual PdfPage AddPage(PdfPage page) {
            CheckClosingStatus();
            CheckAndAddPage(page);
            DispatchEvent(new PdfDocumentEvent(PdfDocumentEvent.INSERT_PAGE, page));
            return page;
        }

        /// <summary>Inserts page to the document.</summary>
        /// <param name="index">position to addPage page to</param>
        /// <param name="page">page to addPage</param>
        /// <returns>inserted page</returns>
        /// <exception cref="iText.Kernel.PdfException">
        /// in case
        /// <paramref name="page"/>
        /// is flushed
        /// </exception>
        public virtual PdfPage AddPage(int index, PdfPage page) {
            CheckClosingStatus();
            CheckAndAddPage(index, page);
            currentPage = page;
            DispatchEvent(new PdfDocumentEvent(PdfDocumentEvent.INSERT_PAGE, page));
            return currentPage;
        }

        /// <summary>Gets number of pages of the document.</summary>
        /// <returns>number of pages.</returns>
        public virtual int GetNumberOfPages() {
            CheckClosingStatus();
            return catalog.GetPageTree().GetNumberOfPages();
        }

        /// <summary>Gets page number by page.</summary>
        /// <param name="page">the page.</param>
        /// <returns>page number.</returns>
        public virtual int GetPageNumber(PdfPage page) {
            CheckClosingStatus();
            return catalog.GetPageTree().GetPageNumber(page);
        }

        /// <summary>
        /// Gets page number by
        /// <see cref="PdfDictionary"/>
        /// .
        /// </summary>
        /// <param name="pageDictionary">
        /// 
        /// <see cref="PdfDictionary"/>
        /// that present page.
        /// </param>
        /// <returns>
        /// page number by
        /// <see cref="PdfDictionary"/>
        /// .
        /// </returns>
        public virtual int GetPageNumber(PdfDictionary pageDictionary) {
            return catalog.GetPageTree().GetPageNumber(pageDictionary);
        }

        /// <summary>
        /// Removes the first occurrence of the specified page from this document,
        /// if it is present.
        /// </summary>
        /// <remarks>
        /// Removes the first occurrence of the specified page from this document,
        /// if it is present. Returns <tt>true</tt> if this document
        /// contained the specified element (or equivalently, if this document
        /// changed as a result of the call).
        /// </remarks>
        /// <param name="page">page to be removed from this document, if present</param>
        /// <returns><tt>true</tt> if this document contained the specified page</returns>
        public virtual bool RemovePage(PdfPage page) {
            CheckClosingStatus();
            int pageNum = GetPageNumber(page);
            return pageNum >= 1 && RemovePage(pageNum) != null;
        }

        /// <summary>Removes page from the document by page number.</summary>
        /// <param name="pageNum">the one-based index of the PdfPage to be removed</param>
        /// <returns>the page that was removed from the list</returns>
        public virtual PdfPage RemovePage(int pageNum) {
            CheckClosingStatus();
            PdfPage removedPage = catalog.GetPageTree().RemovePage(pageNum);
            if (removedPage != null) {
                catalog.RemoveOutlines(removedPage);
                RemoveUnusedWidgetsFromFields(removedPage);
                if (IsTagged()) {
                    GetTagStructureContext().RemovePageTags(removedPage);
                }
                if (!removedPage.GetPdfObject().IsFlushed()) {
                    removedPage.GetPdfObject().Remove(PdfName.Parent);
                }
                removedPage.GetPdfObject().GetIndirectReference().SetFree();
                DispatchEvent(new PdfDocumentEvent(PdfDocumentEvent.REMOVE_PAGE, removedPage));
            }
            return removedPage;
        }

        /// <summary>Gets document information dictionary.</summary>
        /// <returns>document information dictionary.</returns>
        public virtual PdfDocumentInfo GetDocumentInfo() {
            CheckClosingStatus();
            return info;
        }

        /// <summary>Gets default page size.</summary>
        /// <returns>default page size.</returns>
        public virtual PageSize GetDefaultPageSize() {
            return defaultPageSize;
        }

        /// <summary>Sets default page size.</summary>
        /// <param name="pageSize">page size to be set as default.</param>
        public virtual void SetDefaultPageSize(PageSize pageSize) {
            defaultPageSize = pageSize;
        }

        /// <summary><inheritDoc/></summary>
        public virtual void AddEventHandler(String type, IEventHandler handler) {
            eventDispatcher.AddEventHandler(type, handler);
        }

        /// <summary><inheritDoc/></summary>
        public virtual void DispatchEvent(Event @event) {
            eventDispatcher.DispatchEvent(@event);
        }

        /// <summary><inheritDoc/></summary>
        public virtual void DispatchEvent(Event @event, bool delayed) {
            eventDispatcher.DispatchEvent(@event, delayed);
        }

        /// <summary><inheritDoc/></summary>
        public virtual bool HasEventHandler(String type) {
            return eventDispatcher.HasEventHandler(type);
        }

        /// <summary><inheritDoc/></summary>
        public virtual void RemoveEventHandler(String type, IEventHandler handler) {
            eventDispatcher.RemoveEventHandler(type, handler);
        }

        /// <summary><inheritDoc/></summary>
        public virtual void RemoveAllHandlers() {
            eventDispatcher.RemoveAllHandlers();
        }

        /// <summary>
        /// Gets
        /// <c>PdfWriter</c>
        /// associated with the document.
        /// </summary>
        /// <returns>PdfWriter associated with the document.</returns>
        public virtual PdfWriter GetWriter() {
            CheckClosingStatus();
            return writer;
        }

        /// <summary>
        /// Gets
        /// <c>PdfReader</c>
        /// associated with the document.
        /// </summary>
        /// <returns>PdfReader associated with the document.</returns>
        public virtual PdfReader GetReader() {
            CheckClosingStatus();
            return reader;
        }

        /// <summary>
        /// Returns
        /// <see langword="true"/>
        /// if the document is opened in append mode, and
        /// <see langword="false"/>
        /// otherwise.
        /// </summary>
        /// <returns>
        /// 
        /// <see langword="true"/>
        /// if the document is opened in append mode, and
        /// <see langword="false"/>
        /// otherwise.
        /// </returns>
        public virtual bool IsAppendMode() {
            CheckClosingStatus();
            return properties.appendMode;
        }

        /// <summary>Creates next available indirect reference.</summary>
        /// <returns>created indirect reference.</returns>
        public virtual PdfIndirectReference CreateNextIndirectReference() {
            CheckClosingStatus();
            return xref.CreateNextIndirectReference(this);
        }

        /// <summary>Gets PDF version.</summary>
        /// <returns>PDF version.</returns>
        public virtual PdfVersion GetPdfVersion() {
            return pdfVersion;
        }

        /// <summary>Gets PDF catalog.</summary>
        /// <returns>PDF catalog.</returns>
        public virtual PdfCatalog GetCatalog() {
            CheckClosingStatus();
            return catalog;
        }

        /// <summary>Close PDF document.</summary>
        public virtual void Close() {
            if (closed) {
                return;
            }
            isClosing = true;
            try {
                if (writer != null) {
                    if (catalog.IsFlushed()) {
                        throw new PdfException(PdfException.CannotCloseDocumentWithAlreadyFlushedPdfCatalog);
                    }
                    UpdateProducerInInfoDictionary();
                    UpdateXmpMetadata();
                    // In PDF 2.0, all the values except CreationDate and ModDate are deprecated. Remove them now
                    if (pdfVersion.CompareTo(PdfVersion.PDF_2_0) >= 0) {
                        foreach (PdfName deprecatedKey in PdfDocumentInfo.PDF20_DEPRECATED_KEYS) {
                            info.GetPdfObject().Remove(deprecatedKey);
                        }
                    }
                    if (GetXmpMetadata() != null) {
                        PdfStream xmp = catalog.GetPdfObject().GetAsStream(PdfName.Metadata);
                        if (IsAppendMode() && xmp != null && !xmp.IsFlushed() && xmp.GetIndirectReference() != null) {
                            // Use existing object for append mode
                            xmp.SetData(xmpMetadata);
                            xmp.SetModified();
                        }
                        else {
                            // Create new object
                            xmp = ((PdfStream)new PdfStream().MakeIndirect(this));
                            xmp.GetOutputStream().Write(xmpMetadata);
                            catalog.GetPdfObject().Put(PdfName.Metadata, xmp);
                            catalog.SetModified();
                        }
                        xmp.Put(PdfName.Type, PdfName.Metadata);
                        xmp.Put(PdfName.Subtype, PdfName.XML);
                        if (writer.crypto != null && !writer.crypto.IsMetadataEncrypted()) {
                            PdfArray ar = new PdfArray();
                            ar.Add(PdfName.Crypt);
                            xmp.Put(PdfName.Filter, ar);
                        }
                    }
                    CheckIsoConformance();
                    PdfObject crypto = null;
                    if (properties.appendMode) {
                        if (structTreeRoot != null) {
                            TryFlushTagStructure(true);
                        }
                        if (catalog.IsOCPropertiesMayHaveChanged() && catalog.GetOCProperties(false).GetPdfObject().IsModified()) {
                            catalog.GetOCProperties(false).Flush();
                        }
                        if (catalog.pageLabels != null) {
                            catalog.Put(PdfName.PageLabels, catalog.pageLabels.BuildTree());
                        }
                        PdfObject pageRoot = catalog.GetPageTree().GenerateTree();
                        if (catalog.GetPdfObject().IsModified() || pageRoot.IsModified()) {
                            catalog.Put(PdfName.Pages, pageRoot);
                            catalog.GetPdfObject().Flush(false);
                        }
                        foreach (KeyValuePair<PdfName, PdfNameTree> entry in catalog.nameTrees) {
                            PdfNameTree tree = entry.Value;
                            if (tree.IsModified()) {
                                EnsureTreeRootAddedToNames(((PdfDictionary)tree.BuildTree().MakeIndirect(this)), entry.Key);
                            }
                        }
                        if (info.GetPdfObject().IsModified()) {
                            info.GetPdfObject().Flush(false);
                        }
                        FlushFonts();
                        writer.FlushModifiedWaitingObjects();
                        if (writer.crypto != null) {
                            System.Diagnostics.Debug.Assert(reader.decrypt.GetPdfObject() == writer.crypto.GetPdfObject(), "Conflict with source encryption"
                                );
                            crypto = reader.decrypt.GetPdfObject();
                        }
                    }
                    else {
                        if (catalog.IsOCPropertiesMayHaveChanged()) {
                            catalog.GetPdfObject().Put(PdfName.OCProperties, catalog.GetOCProperties(false).GetPdfObject());
                            catalog.GetOCProperties(false).Flush();
                        }
                        if (catalog.pageLabels != null) {
                            catalog.Put(PdfName.PageLabels, catalog.pageLabels.BuildTree());
                        }
                        catalog.GetPdfObject().Put(PdfName.Pages, catalog.GetPageTree().GenerateTree());
                        foreach (KeyValuePair<PdfName, PdfNameTree> entry in catalog.nameTrees) {
                            PdfNameTree tree = entry.Value;
                            if (tree.IsModified()) {
                                EnsureTreeRootAddedToNames(((PdfDictionary)tree.BuildTree().MakeIndirect(this)), entry.Key);
                            }
                        }
                        for (int pageNum = 1; pageNum <= GetNumberOfPages(); pageNum++) {
                            GetPage(pageNum).Flush();
                        }
                        if (structTreeRoot != null) {
                            TryFlushTagStructure(false);
                        }
                        catalog.GetPdfObject().Flush(false);
                        info.GetPdfObject().Flush(false);
                        FlushFonts();
                        writer.FlushWaitingObjects();
                        // flush unused objects
                        for (int i = 0; i < xref.Size(); i++) {
                            PdfIndirectReference indirectReference = xref.Get(i);
                            if (indirectReference != null && !indirectReference.IsFree() && !indirectReference.CheckState(PdfObject.FLUSHED
                                )) {
                                if (IsFlushUnusedObjects() && !indirectReference.CheckState(PdfObject.ORIGINAL_OBJECT_STREAM)) {
                                    PdfObject @object = indirectReference.GetRefersTo();
                                    @object.Flush();
                                }
                                else {
                                    indirectReference.SetFree();
                                }
                            }
                        }
                    }
                    PdfObject fileId = GetFileId(crypto, writer.properties);
                    if (crypto == null && writer.crypto != null) {
                        crypto = writer.crypto.GetPdfObject();
                        crypto.MakeIndirect(this);
                        // To avoid encryption of XrefStream and Encryption dictionary remove crypto.
                        // NOTE. No need in reverting, because it is the last operation with the document.
                        writer.crypto = null;
                        crypto.Flush(false);
                    }
                    // The following two operators prevents the possible inconsistency between root and info
                    // entries existing in the trailer object and corresponding fields. This inconsistency
                    // may appear when user gets trailer and explicitly sets new root or info dictionaries.
                    trailer.Put(PdfName.Root, catalog.GetPdfObject());
                    trailer.Put(PdfName.Info, info.GetPdfObject());
                    xref.WriteXrefTableAndTrailer(this, fileId, crypto);
                    writer.Flush();
                    Counter counter = GetCounter();
                    if (counter != null) {
                        counter.OnDocumentWritten(writer.GetCurrentPos());
                    }
                }
                catalog.GetPageTree().ClearPageRefs();
                RemoveAllHandlers();
            }
            catch (System.IO.IOException e) {
                throw new PdfException(PdfException.CannotCloseDocument, e, this);
            }
            finally {
                if (writer != null && IsCloseWriter()) {
                    try {
                        writer.Dispose();
                    }
                    catch (Exception e) {
                        ILogger logger = LoggerFactory.GetLogger(typeof(iText.Kernel.Pdf.PdfDocument));
                        logger.Error(iText.IO.LogMessageConstant.PDF_WRITER_CLOSING_FAILED, e);
                    }
                }
                if (reader != null && IsCloseReader()) {
                    try {
                        reader.Close();
                    }
                    catch (Exception e) {
                        ILogger logger = LoggerFactory.GetLogger(typeof(iText.Kernel.Pdf.PdfDocument));
                        logger.Error(iText.IO.LogMessageConstant.PDF_READER_CLOSING_FAILED, e);
                    }
                }
            }
            closed = true;
        }

        private PdfObject GetFileId(PdfObject crypto, WriterProperties properties) {
            bool isModified = false;
            byte[] originalFileID = null;
            if (properties.initialDocumentId != null) {
                originalFileID = ByteUtils.GetIsoBytes(properties.initialDocumentId.GetValue());
            }
            else {
                if (initialDocumentId != null) {
                    originalFileID = ByteUtils.GetIsoBytes(initialDocumentId.GetValue());
                }
            }
            if (originalFileID == null && crypto == null && writer.crypto != null) {
                originalFileID = writer.crypto.GetDocumentId();
            }
            if (originalFileID == null && GetReader() != null) {
                originalFileID = GetReader().GetOriginalFileId();
                isModified = true;
            }
            if (originalFileID == null) {
                originalFileID = PdfEncryption.GenerateNewDocumentId();
            }
            byte[] secondId = null;
            if (properties.modifiedDocumentId != null) {
                secondId = ByteUtils.GetIsoBytes(properties.modifiedDocumentId.GetValue());
            }
            else {
                if (modifiedDocumentId != null) {
                    secondId = ByteUtils.GetIsoBytes(modifiedDocumentId.GetValue());
                }
            }
            if (secondId == null && originalModifiedDocumentId != null) {
                PdfString newModifiedId = reader.trailer.GetAsArray(PdfName.ID).GetAsString(1);
                if (!originalModifiedDocumentId.Equals(newModifiedId)) {
                    secondId = ByteUtils.GetIsoBytes(newModifiedId.GetValue());
                }
                else {
                    secondId = PdfEncryption.GenerateNewDocumentId();
                }
            }
            if (secondId == null) {
                secondId = (isModified) ? PdfEncryption.GenerateNewDocumentId() : originalFileID;
            }
            return PdfEncryption.CreateInfoId(originalFileID, secondId);
        }

        /// <summary>Gets close status of the document.</summary>
        /// <returns>true, if the document has already been closed, otherwise false.</returns>
        public virtual bool IsClosed() {
            return closed;
        }

        /// <summary>Gets tagged status of the document.</summary>
        /// <returns>true, if the document has tag structure, otherwise false.</returns>
        public virtual bool IsTagged() {
            return structTreeRoot != null;
        }

        public virtual void SetTagged() {
            CheckClosingStatus();
            if (structTreeRoot == null) {
                structTreeRoot = new PdfStructTreeRoot(this);
                catalog.GetPdfObject().Put(PdfName.StructTreeRoot, structTreeRoot.GetPdfObject());
                UpdateValueInMarkInfoDict(PdfName.Marked, PdfBoolean.TRUE);
                structParentIndex = 0;
            }
        }

        /// <summary>
        /// Gets
        /// <see cref="iText.Kernel.Pdf.Tagging.PdfStructTreeRoot"/>
        /// of tagged document.
        /// </summary>
        /// <returns>
        /// 
        /// <see cref="iText.Kernel.Pdf.Tagging.PdfStructTreeRoot"/>
        /// in case tagged document, otherwise false.
        /// </returns>
        /// <seealso cref="IsTagged()"/>
        /// <seealso cref="GetNextStructParentIndex()"/>
        public virtual PdfStructTreeRoot GetStructTreeRoot() {
            return structTreeRoot;
        }

        /// <summary>Gets next parent index of tagged document.</summary>
        /// <returns>-1 if document is not tagged, or &gt;= 0 if tagged.</returns>
        /// <seealso cref="IsTagged()"/>
        /// <seealso cref="GetNextStructParentIndex()"/>
        public virtual int GetNextStructParentIndex() {
            return structParentIndex < 0 ? -1 : structParentIndex++;
        }

        /// <summary>
        /// Gets document
        /// <c>TagStructureContext</c>
        /// .
        /// The document must be tagged, otherwise an exception will be thrown.
        /// </summary>
        /// <returns>
        /// document
        /// <c>TagStructureContext</c>
        /// .
        /// </returns>
        public virtual TagStructureContext GetTagStructureContext() {
            CheckClosingStatus();
            if (tagStructureContext == null) {
                if (!IsTagged()) {
                    throw new PdfException(PdfException.MustBeATaggedDocument);
                }
                InitTagStructureContext();
            }
            return tagStructureContext;
        }

        /// <summary>
        /// Copies a range of pages from current document to
        /// <paramref name="toDocument"/>
        /// .
        /// Use this method if you want to copy pages across tagged documents.
        /// This will keep resultant PDF structure consistent.
        /// </summary>
        /// <param name="pageFrom">start of the range of pages to be copied.</param>
        /// <param name="pageTo">end of the range of pages to be copied.</param>
        /// <param name="toDocument">a document to copy pages to.</param>
        /// <param name="insertBeforePage">a position where to insert copied pages.</param>
        /// <returns>list of copied pages</returns>
        public virtual IList<PdfPage> CopyPagesTo(int pageFrom, int pageTo, iText.Kernel.Pdf.PdfDocument toDocument
            , int insertBeforePage) {
            return CopyPagesTo(pageFrom, pageTo, toDocument, insertBeforePage, null);
        }

        /// <summary>
        /// Copies a range of pages from current document to
        /// <paramref name="toDocument"/>
        /// .
        /// Use this method if you want to copy pages across tagged documents.
        /// This will keep resultant PDF structure consistent.
        /// </summary>
        /// <param name="pageFrom">1-based start of the range of pages to be copied.</param>
        /// <param name="pageTo">1-based end of the range of pages to be copied.</param>
        /// <param name="toDocument">a document to copy pages to.</param>
        /// <param name="insertBeforePage">a position where to insert copied pages.</param>
        /// <param name="copier">
        /// a copier which bears a special copy logic. May be null.
        /// It is recommended to use the same instance of
        /// <see cref="IPdfPageExtraCopier"/>
        /// for the same output document.
        /// </param>
        /// <returns>list of new copied pages</returns>
        public virtual IList<PdfPage> CopyPagesTo(int pageFrom, int pageTo, iText.Kernel.Pdf.PdfDocument toDocument
            , int insertBeforePage, IPdfPageExtraCopier copier) {
            IList<int> pages = new List<int>();
            for (int i = pageFrom; i <= pageTo; i++) {
                pages.Add(i);
            }
            return CopyPagesTo(pages, toDocument, insertBeforePage, copier);
        }

        /// <summary>
        /// Copies a range of pages from current document to
        /// <paramref name="toDocument"/>
        /// appending copied pages to the end.
        /// Use this method if you want to copy pages across tagged documents.
        /// This will keep resultant PDF structure consistent.
        /// </summary>
        /// <param name="pageFrom">1-based start of the range of pages to be copied.</param>
        /// <param name="pageTo">1-based end of the range of pages to be copied.</param>
        /// <param name="toDocument">a document to copy pages to.</param>
        /// <returns>list of new copied pages</returns>
        public virtual IList<PdfPage> CopyPagesTo(int pageFrom, int pageTo, iText.Kernel.Pdf.PdfDocument toDocument
            ) {
            return CopyPagesTo(pageFrom, pageTo, toDocument, null);
        }

        /// <summary>
        /// Copies a range of pages from current document to
        /// <paramref name="toDocument"/>
        /// appending copied pages to the end.
        /// Use this method if you want to copy pages across tagged documents.
        /// This will keep resultant PDF structure consistent.
        /// </summary>
        /// <param name="pageFrom">1-based start of the range of pages to be copied.</param>
        /// <param name="pageTo">1-based end of the range of pages to be copied.</param>
        /// <param name="toDocument">a document to copy pages to.</param>
        /// <param name="copier">
        /// a copier which bears a special copy logic. May be null.
        /// It is recommended to use the same instance of
        /// <see cref="IPdfPageExtraCopier"/>
        /// for the same output document.
        /// </param>
        /// <returns>list of new copied pages.</returns>
        public virtual IList<PdfPage> CopyPagesTo(int pageFrom, int pageTo, iText.Kernel.Pdf.PdfDocument toDocument
            , IPdfPageExtraCopier copier) {
            return CopyPagesTo(pageFrom, pageTo, toDocument, toDocument.GetNumberOfPages() + 1, copier);
        }

        /// <summary>
        /// Copies a range of pages from current document to
        /// <paramref name="toDocument"/>
        /// .
        /// Use this method if you want to copy pages across tagged documents.
        /// This will keep resultant PDF structure consistent.
        /// </summary>
        /// <param name="pagesToCopy">list of pages to be copied. TreeSet for the order of the pages to be natural.</param>
        /// <param name="toDocument">a document to copy pages to.</param>
        /// <param name="insertBeforePage">a position where to insert copied pages.</param>
        /// <returns>list of new copied pages</returns>
        public virtual IList<PdfPage> CopyPagesTo(IList<int> pagesToCopy, iText.Kernel.Pdf.PdfDocument toDocument, 
            int insertBeforePage) {
            return CopyPagesTo(pagesToCopy, toDocument, insertBeforePage, null);
        }

        /// <summary>
        /// Copies a range of pages from current document to
        /// <paramref name="toDocument"/>
        /// .
        /// Use this method if you want to copy pages across tagged documents.
        /// This will keep resultant PDF structure consistent.
        /// </summary>
        /// <param name="pagesToCopy">list of pages to be copied. TreeSet for the order of the pages to be natural.</param>
        /// <param name="toDocument">a document to copy pages to.</param>
        /// <param name="insertBeforePage">a position where to insert copied pages.</param>
        /// <param name="copier">
        /// a copier which bears a special copy logic. May be null.
        /// It is recommended to use the same instance of
        /// <see cref="IPdfPageExtraCopier"/>
        /// for the same output document.
        /// </param>
        /// <returns>list of new copied pages</returns>
        public virtual IList<PdfPage> CopyPagesTo(IList<int> pagesToCopy, iText.Kernel.Pdf.PdfDocument toDocument, 
            int insertBeforePage, IPdfPageExtraCopier copier) {
            if (pagesToCopy.IsEmpty()) {
                return JavaCollectionsUtil.EmptyList<PdfPage>();
            }
            CheckClosingStatus();
            IList<PdfPage> copiedPages = new List<PdfPage>();
            IDictionary<PdfPage, PdfPage> page2page = new LinkedDictionary<PdfPage, PdfPage>();
            ICollection<PdfOutline> outlinesToCopy = new HashSet<PdfOutline>();
            IList<IDictionary<PdfPage, PdfPage>> rangesOfPagesWithIncreasingNumbers = new List<IDictionary<PdfPage, PdfPage
                >>();
            int lastCopiedPageNum = (int)pagesToCopy[0];
            int pageInsertIndex = insertBeforePage;
            bool insertInBetween = insertBeforePage < toDocument.GetNumberOfPages() + 1;
            foreach (int? pageNum in pagesToCopy) {
                PdfPage page = GetPage((int)pageNum);
                PdfPage newPage = page.CopyTo(toDocument, copier);
                copiedPages.Add(newPage);
                if (!page2page.ContainsKey(page)) {
                    page2page.Put(page, newPage);
                }
                if (lastCopiedPageNum >= pageNum) {
                    rangesOfPagesWithIncreasingNumbers.Add(new Dictionary<PdfPage, PdfPage>());
                }
                int lastRangeInd = rangesOfPagesWithIncreasingNumbers.Count - 1;
                rangesOfPagesWithIncreasingNumbers[lastRangeInd].Put(page, newPage);
                if (insertInBetween) {
                    toDocument.AddPage(pageInsertIndex, newPage);
                }
                else {
                    toDocument.AddPage(newPage);
                }
                pageInsertIndex++;
                if (toDocument.HasOutlines()) {
                    IList<PdfOutline> pageOutlines = page.GetOutlines(false);
                    if (pageOutlines != null) {
                        outlinesToCopy.AddAll(pageOutlines);
                    }
                }
                lastCopiedPageNum = (int)pageNum;
            }
            CopyLinkAnnotations(toDocument, page2page);
            // It's important to copy tag structure after link annotations were copied, because object content items in tag
            // structure are not copied in case if their's OBJ key is annotation and doesn't contain /P entry.
            if (toDocument.IsTagged()) {
                if (IsTagged()) {
                    try {
                        foreach (IDictionary<PdfPage, PdfPage> increasingPagesRange in rangesOfPagesWithIncreasingNumbers) {
                            if (insertInBetween) {
                                GetStructTreeRoot().CopyTo(toDocument, insertBeforePage, increasingPagesRange);
                            }
                            else {
                                GetStructTreeRoot().CopyTo(toDocument, increasingPagesRange);
                            }
                            insertBeforePage += increasingPagesRange.Count;
                        }
                        toDocument.GetTagStructureContext().NormalizeDocumentRootTag();
                    }
                    catch (Exception ex) {
                        throw new PdfException(PdfException.TagStructureCopyingFailedItMightBeCorruptedInOneOfTheDocuments, ex);
                    }
                }
                else {
                    ILogger logger = LoggerFactory.GetLogger(typeof(iText.Kernel.Pdf.PdfDocument));
                    logger.Warn(iText.IO.LogMessageConstant.NOT_TAGGED_PAGES_IN_TAGGED_DOCUMENT);
                }
            }
            if (catalog.IsOutlineMode()) {
                CopyOutlines(outlinesToCopy, toDocument, page2page);
            }
            return copiedPages;
        }

        /// <summary>
        /// Copies a range of pages from current document to
        /// <paramref name="toDocument"/>
        /// appending copied pages to the end.
        /// Use this method if you want to copy pages across tagged documents.
        /// This will keep resultant PDF structure consistent.
        /// </summary>
        /// <param name="pagesToCopy">list of pages to be copied. TreeSet for the order of the pages to be natural.</param>
        /// <param name="toDocument">a document to copy pages to.</param>
        /// <returns>list of copied pages</returns>
        public virtual IList<PdfPage> CopyPagesTo(IList<int> pagesToCopy, iText.Kernel.Pdf.PdfDocument toDocument) {
            return CopyPagesTo(pagesToCopy, toDocument, null);
        }

        /// <summary>
        /// Copies a range of pages from current document to
        /// <paramref name="toDocument"/>
        /// appending copied pages to the end.
        /// Use this method if you want to copy pages across tagged documents.
        /// This will keep resultant PDF structure consistent.
        /// </summary>
        /// <param name="pagesToCopy">list of pages to be copied. TreeSet for the order of the pages to be natural.</param>
        /// <param name="toDocument">a document to copy pages to.</param>
        /// <param name="copier">
        /// a copier which bears a special copy logic. May be null.
        /// It is recommended to use the same instance of
        /// <see cref="IPdfPageExtraCopier"/>
        /// for the same output document.
        /// </param>
        /// <returns>list of copied pages</returns>
        public virtual IList<PdfPage> CopyPagesTo(IList<int> pagesToCopy, iText.Kernel.Pdf.PdfDocument toDocument, 
            IPdfPageExtraCopier copier) {
            return CopyPagesTo(pagesToCopy, toDocument, toDocument.GetNumberOfPages() + 1, copier);
        }

        /// <summary>Flush all copied objects and remove them from copied cache.</summary>
        /// <remarks>
        /// Flush all copied objects and remove them from copied cache.
        /// Note, if you will copy objects from the same document, doublicated objects will be created.
        /// </remarks>
        /// <param name="sourceDoc">source document</param>
        public virtual void FlushCopiedObjects(iText.Kernel.Pdf.PdfDocument sourceDoc) {
            if (GetWriter() != null) {
                GetWriter().FlushCopiedObjects(sourceDoc.GetDocumentId());
            }
        }

        /// <summary>
        /// Checks, whether
        /// <see cref="Close()"/>
        /// method will close associated PdfReader.
        /// </summary>
        /// <returns>
        /// true,
        /// <see cref="Close()"/>
        /// method is going to close associated PdfReader, otherwise false.
        /// </returns>
        public virtual bool IsCloseReader() {
            return closeReader;
        }

        /// <summary>
        /// Sets, whether
        /// <see cref="Close()"/>
        /// method shall close associated PdfReader.
        /// </summary>
        /// <param name="closeReader">
        /// true,
        /// <see cref="Close()"/>
        /// method shall close associated PdfReader, otherwise false.
        /// </param>
        public virtual void SetCloseReader(bool closeReader) {
            CheckClosingStatus();
            this.closeReader = closeReader;
        }

        /// <summary>
        /// Checks, whether
        /// <see cref="Close()"/>
        /// method will close associated PdfWriter.
        /// </summary>
        /// <returns>
        /// true,
        /// <see cref="Close()"/>
        /// method is going to close associated PdfWriter, otherwise false.
        /// </returns>
        public virtual bool IsCloseWriter() {
            return closeWriter;
        }

        /// <summary>
        /// Sets, whether
        /// <see cref="Close()"/>
        /// method shall close associated PdfWriter.
        /// </summary>
        /// <param name="closeWriter">
        /// true,
        /// <see cref="Close()"/>
        /// method shall close associated PdfWriter, otherwise false.
        /// </param>
        public virtual void SetCloseWriter(bool closeWriter) {
            CheckClosingStatus();
            this.closeWriter = closeWriter;
        }

        /// <summary>
        /// Checks, whether
        /// <see cref="Close()"/>
        /// will flush unused objects,
        /// e.g. unreachable from PDF Catalog. By default - false.
        /// </summary>
        /// <returns>
        /// false, if
        /// <see cref="Close()"/>
        /// shall not flush unused objects, otherwise true.
        /// </returns>
        public virtual bool IsFlushUnusedObjects() {
            return flushUnusedObjects;
        }

        /// <summary>
        /// Sets, whether
        /// <see cref="Close()"/>
        /// shall flush unused objects,
        /// e.g. unreachable from PDF Catalog.
        /// </summary>
        /// <param name="flushUnusedObjects">
        /// false, if
        /// <see cref="Close()"/>
        /// shall not flush unused objects, otherwise true.
        /// </param>
        public virtual void SetFlushUnusedObjects(bool flushUnusedObjects) {
            CheckClosingStatus();
            this.flushUnusedObjects = flushUnusedObjects;
        }

        /// <summary>This method returns a complete outline tree of the whole document.</summary>
        /// <param name="updateOutlines">
        /// if the flag is true, the method read the whole document and creates outline tree.
        /// If false the method gets cached outline tree (if it was cached via calling getOutlines method before).
        /// </param>
        /// <returns>
        /// fully initialize
        /// <see cref="PdfOutline"/>
        /// object.
        /// </returns>
        public virtual PdfOutline GetOutlines(bool updateOutlines) {
            CheckClosingStatus();
            return catalog.GetOutlines(updateOutlines);
        }

        /// <summary>This method initializes an outline tree of the document and sets outline mode to true.</summary>
        public virtual void InitializeOutlines() {
            CheckClosingStatus();
            GetOutlines(false);
        }

        /// <summary>This methods adds new name in the Dests NameTree.</summary>
        /// <remarks>This methods adds new name in the Dests NameTree. It throws an exception, if the name already exists.
        ///     </remarks>
        /// <param name="key">Name of the destination.</param>
        /// <param name="value">
        /// An object destination refers to. Must be an array or a dictionary with key /D and array.
        /// See ISO 32000-1 12.3.2.3 for more info.
        /// </param>
        public virtual void AddNamedDestination(String key, PdfObject value) {
            CheckClosingStatus();
            catalog.AddNamedDestination(key, value);
        }

        /// <summary>Gets static copy of cross reference table.</summary>
        public virtual IList<PdfIndirectReference> ListIndirectReferences() {
            CheckClosingStatus();
            IList<PdfIndirectReference> indRefs = new List<PdfIndirectReference>(xref.Size());
            for (int i = 0; i < xref.Size(); ++i) {
                PdfIndirectReference indref = xref.Get(i);
                if (indref != null) {
                    indRefs.Add(indref);
                }
            }
            return indRefs;
        }

        /// <summary>Gets document trailer.</summary>
        /// <returns>document trailer.</returns>
        public virtual PdfDictionary GetTrailer() {
            CheckClosingStatus();
            return trailer;
        }

        /// <summary>
        /// Adds
        /// <see cref="PdfOutputIntent"/>
        /// that shall specify the colour characteristics of output devices
        /// on which the document might be rendered.
        /// </summary>
        /// <param name="outputIntent">
        /// 
        /// <see cref="PdfOutputIntent"/>
        /// to add.
        /// </param>
        /// <seealso cref="PdfOutputIntent"/>
        public virtual void AddOutputIntent(PdfOutputIntent outputIntent) {
            CheckClosingStatus();
            if (outputIntent == null) {
                return;
            }
            PdfArray outputIntents = catalog.GetPdfObject().GetAsArray(PdfName.OutputIntents);
            if (outputIntents == null) {
                outputIntents = new PdfArray();
                catalog.Put(PdfName.OutputIntents, outputIntents);
            }
            outputIntents.Add(outputIntent.GetPdfObject());
        }

        /// <summary>Checks whether PDF document conforms a specific standard.</summary>
        /// <remarks>
        /// Checks whether PDF document conforms a specific standard.
        /// Shall be override.
        /// </remarks>
        /// <param name="obj">An object to conform.</param>
        /// <param name="key">type of object to conform.</param>
        public virtual void CheckIsoConformance(Object obj, IsoKey key) {
        }

        /// <summary>Checks whether PDF document conforms a specific standard.</summary>
        /// <remarks>
        /// Checks whether PDF document conforms a specific standard.
        /// Shall be override.
        /// </remarks>
        /// <param name="obj">an object to conform.</param>
        /// <param name="key">type of object to conform.</param>
        /// <param name="resources">
        /// 
        /// <see cref="PdfResources"/>
        /// associated with an object to check.
        /// </param>
        public virtual void CheckIsoConformance(Object obj, IsoKey key, PdfResources resources) {
        }

        /// <summary>Checks whether PDF document conforms a specific standard.</summary>
        /// <remarks>
        /// Checks whether PDF document conforms a specific standard.
        /// Shall be override.
        /// </remarks>
        /// <param name="gState">
        /// a
        /// <see cref="iText.Kernel.Pdf.Canvas.CanvasGraphicsState"/>
        /// object to conform.
        /// </param>
        /// <param name="resources">
        /// 
        /// <see cref="PdfResources"/>
        /// associated with an object to check.
        /// </param>
        public virtual void CheckShowTextIsoConformance(Object gState, PdfResources resources) {
        }

        /// <summary>Adds file attachment at document level.</summary>
        /// <param name="description">the file description</param>
        /// <param name="fileStore">an array with the file.</param>
        /// <param name="fileDisplay">the actual file name stored in the pdf</param>
        /// <param name="mimeType">mime type of the file</param>
        /// <param name="fileParameter">the optional extra file parameters such as the creation or modification date</param>
        /// <param name="afRelationshipValue">
        /// if
        /// <see langword="null"/>
        /// ,
        /// <see cref="PdfName.Unspecified"/>
        /// will be added. Shall be one of:
        /// <see cref="PdfName.Source"/>
        /// ,
        /// <see cref="PdfName.Data"/>
        /// ,
        /// <see cref="PdfName.Alternative"/>
        /// ,
        /// <see cref="PdfName.Supplement"/>
        /// or
        /// <see cref="PdfName.Unspecified"/>
        /// .
        /// </param>
        [System.ObsoleteAttribute(@"use AddAssociatedFile(System.String, iText.Kernel.Pdf.Filespec.PdfFileSpec) methods. Will be removed in iText 7.1"
            )]
        public virtual void AddFileAttachment(String description, byte[] fileStore, String fileDisplay, PdfName mimeType
            , PdfDictionary fileParameter, PdfName afRelationshipValue) {
            AddFileAttachment(description, PdfFileSpec.CreateEmbeddedFileSpec(this, fileStore, description, fileDisplay
                , mimeType, fileParameter, afRelationshipValue));
        }

        /// <summary>Adds file attachment at document level.</summary>
        /// <param name="description">the file description</param>
        /// <param name="file">the path to the file.</param>
        /// <param name="fileDisplay">the actual file name stored in the pdf</param>
        /// <param name="mimeType">mime type of the file</param>
        /// <param name="afRelationshipValue">
        /// if
        /// <see langword="null"/>
        /// ,
        /// <see cref="PdfName.Unspecified"/>
        /// will be added. Shall be one of:
        /// <see cref="PdfName.Source"/>
        /// ,
        /// <see cref="PdfName.Data"/>
        /// ,
        /// <see cref="PdfName.Alternative"/>
        /// ,
        /// <see cref="PdfName.Supplement"/>
        /// or
        /// <see cref="PdfName.Unspecified"/>
        /// .
        /// </param>
        /// <exception cref="System.IO.IOException"/>
        [System.ObsoleteAttribute(@"use AddAssociatedFile(System.String, iText.Kernel.Pdf.Filespec.PdfFileSpec) methods. Will be removed in iText 7.1"
            )]
        public virtual void AddFileAttachment(String description, String file, String fileDisplay, PdfName mimeType
            , PdfName afRelationshipValue) {
            AddFileAttachment(description, PdfFileSpec.CreateEmbeddedFileSpec(this, file, description, fileDisplay, mimeType
                , afRelationshipValue));
        }

        /// <summary>Adds file attachment at document level.</summary>
        /// <param name="description">the file description</param>
        /// <param name="fs">
        /// 
        /// <see cref="iText.Kernel.Pdf.Filespec.PdfFileSpec"/>
        /// object.
        /// </param>
        public virtual void AddFileAttachment(String description, PdfFileSpec fs) {
            CheckClosingStatus();
            catalog.AddNameToNameTree(description, fs.GetPdfObject(), PdfName.EmbeddedFiles);
        }

        /// <summary>
        /// <p>
        /// Adds file associated with PDF document as a whole and identifies the relationship between them.
        /// </summary>
        /// <remarks>
        /// <p>
        /// Adds file associated with PDF document as a whole and identifies the relationship between them.
        /// </p>
        /// <p>
        /// Associated files may be used in Pdf/A-3 and Pdf 2.0 documents.
        /// The method is very similar to
        /// <see cref="AddFileAttachment(System.String, iText.Kernel.Pdf.Filespec.PdfFileSpec)"/>
        /// .
        /// However, besides adding file description to Names tree, it adds file to array value of the AF key in the document catalog.
        /// </p>
        /// <p>
        /// For associated files their associated file specification dictionaries shall include the AFRelationship key
        /// </p>
        /// </remarks>
        /// <param name="description">the file description</param>
        /// <param name="fs">file specification dictionary of associated file</param>
        /// <seealso cref="AddFileAttachment(System.String, iText.Kernel.Pdf.Filespec.PdfFileSpec)"/>
        public virtual void AddAssociatedFile(String description, PdfFileSpec fs) {
            if (null == ((PdfDictionary)fs.GetPdfObject()).Get(PdfName.AFRelationship)) {
                ILogger logger = LoggerFactory.GetLogger(typeof(iText.Kernel.Pdf.PdfDocument));
                logger.Error(iText.IO.LogMessageConstant.ASSOCIATED_FILE_SPEC_SHALL_INCLUDE_AFRELATIONSHIP);
            }
            PdfArray afArray = catalog.GetPdfObject().GetAsArray(PdfName.AF);
            if (afArray == null) {
                afArray = ((PdfArray)new PdfArray().MakeIndirect(this));
                catalog.Put(PdfName.AF, afArray);
            }
            afArray.Add(fs.GetPdfObject());
            AddFileAttachment(description, fs);
        }

        /// <summary>Returns files associated with PDF document.</summary>
        /// <returns>associated files array.</returns>
        public virtual PdfArray GetAssociatedFiles() {
            CheckClosingStatus();
            return catalog.GetPdfObject().GetAsArray(PdfName.AF);
        }

        /// <summary>This method retrieves the page labels from a document as an array of String objects.</summary>
        /// <returns>
        /// 
        /// <see cref="System.String"/>
        /// list of page labels if they were found, or
        /// <see langword="null"/>
        /// otherwise
        /// </returns>
        public virtual String[] GetPageLabels() {
            if (catalog.GetPageLabelsTree(false) == null) {
                return null;
            }
            IDictionary<int?, PdfObject> pageLabels = catalog.GetPageLabelsTree(false).GetNumbers();
            if (pageLabels.Count == 0) {
                return null;
            }
            String[] labelStrings = new String[GetNumberOfPages()];
            int pageCount = 1;
            String prefix = "";
            String type = "D";
            for (int i = 0; i < GetNumberOfPages(); i++) {
                if (pageLabels.ContainsKey(i)) {
                    PdfDictionary labelDictionary = (PdfDictionary)pageLabels.Get(i);
                    PdfNumber pageRange = labelDictionary.GetAsNumber(PdfName.St);
                    if (pageRange != null) {
                        pageCount = pageRange.IntValue();
                    }
                    else {
                        pageCount = 1;
                    }
                    PdfString p = labelDictionary.GetAsString(PdfName.P);
                    if (p != null) {
                        prefix = p.ToUnicodeString();
                    }
                    else {
                        prefix = "";
                    }
                    PdfName t = labelDictionary.GetAsName(PdfName.S);
                    if (t != null) {
                        type = t.GetValue();
                    }
                    else {
                        type = "e";
                    }
                }
                switch (type) {
                    case "R": {
                        labelStrings[i] = prefix + RomanNumbering.ToRomanUpperCase(pageCount);
                        break;
                    }

                    case "r": {
                        labelStrings[i] = prefix + RomanNumbering.ToRomanLowerCase(pageCount);
                        break;
                    }

                    case "A": {
                        labelStrings[i] = prefix + EnglishAlphabetNumbering.ToLatinAlphabetNumberUpperCase(pageCount);
                        break;
                    }

                    case "a": {
                        labelStrings[i] = prefix + EnglishAlphabetNumbering.ToLatinAlphabetNumberLowerCase(pageCount);
                        break;
                    }

                    case "e": {
                        labelStrings[i] = prefix;
                        break;
                    }

                    default: {
                        labelStrings[i] = prefix + pageCount;
                        break;
                    }
                }
                pageCount++;
            }
            return labelStrings;
        }

        /// <summary>Indicates if the document has any outlines</summary>
        /// <returns>
        /// 
        /// <see langword="true"/>
        /// , if there are outlines and
        /// <see langword="false"/>
        /// otherwise.
        /// </returns>
        public virtual bool HasOutlines() {
            return catalog.HasOutlines();
        }

        /// <summary>Sets the flag indicating the presence of structure elements that contain user properties attributes.
        ///     </summary>
        /// <param name="userProperties">the user properties flag</param>
        public virtual void SetUserProperties(bool userProperties) {
            this.userProperties = userProperties;
            PdfBoolean userPropsVal = userProperties ? PdfBoolean.TRUE : PdfBoolean.FALSE;
            UpdateValueInMarkInfoDict(PdfName.UserProperties, userPropsVal);
        }

        /// <summary>The /ID entry of a document contains an array with two entries.</summary>
        /// <remarks>
        /// The /ID entry of a document contains an array with two entries. The first one represents the initial document id.
        /// The second one should be the same entry, unless the document has been modified. iText will by default keep thi
        /// existing initial id. But if you'd like you can set this id yourself using this setter.
        /// </remarks>
        /// <param name="initialDocumentId">the new initial document id</param>
        [System.ObsoleteAttribute(@"Will be removed in 7.1. Use WriterProperties.SetInitialDocumentId(PdfString) instead"
            )]
        public virtual void SetInitialDocumentId(PdfString initialDocumentId) {
            this.initialDocumentId = initialDocumentId;
        }

        /// <summary>The /ID entry of a document contains an array with two entries.</summary>
        /// <remarks>
        /// The /ID entry of a document contains an array with two entries. The first one represents the initial document id.
        /// The second one should be the same entry, unless the document has been modified. iText will by default generate
        /// a modified id. But if you'd like you can set this id yourself using this setter.
        /// </remarks>
        /// <param name="modifiedDocumentId">the new modified document id</param>
        [System.ObsoleteAttribute(@"Will be removed in 7.1. Use WriterProperties.SetModifiedDocumentId(PdfString) instead"
            )]
        public virtual void SetModifiedDocumentId(PdfString modifiedDocumentId) {
            this.modifiedDocumentId = modifiedDocumentId;
        }

        /// <summary>
        /// Create a new instance of
        /// <see cref="iText.Kernel.Font.PdfFont"/>
        /// or load already created one.
        /// <p>
        /// Note, PdfFont which created with
        /// <see cref="iText.Kernel.Font.PdfFontFactory.CreateFont(PdfDictionary)"/>
        /// won't be cached
        /// until it will be added to
        /// <see cref="iText.Kernel.Pdf.Canvas.PdfCanvas"/>
        /// or
        /// <see cref="PdfResources"/>
        /// .
        /// </summary>
        public virtual PdfFont GetFont(PdfDictionary dictionary) {
            System.Diagnostics.Debug.Assert(dictionary.GetIndirectReference() != null);
            if (documentFonts.ContainsKey(dictionary.GetIndirectReference())) {
                return documentFonts.Get(dictionary.GetIndirectReference());
            }
            else {
                return AddFont(PdfFontFactory.CreateFont(dictionary));
            }
        }

        /// <summary>Gets default font for the document: Helvetica, WinAnsi.</summary>
        /// <remarks>
        /// Gets default font for the document: Helvetica, WinAnsi.
        /// One instance per document.
        /// </remarks>
        /// <returns>
        /// instance of
        /// <see cref="iText.Kernel.Font.PdfFont"/>
        /// or
        /// <see langword="null"/>
        /// on error.
        /// </returns>
        public virtual PdfFont GetDefaultFont() {
            if (defaultFont == null) {
                try {
                    defaultFont = PdfFontFactory.CreateFont();
                    AddFont(defaultFont);
                }
                catch (System.IO.IOException e) {
                    ILogger logger = LoggerFactory.GetLogger(typeof(iText.Kernel.Pdf.PdfDocument));
                    logger.Error(iText.IO.LogMessageConstant.EXCEPTION_WHILE_CREATING_DEFAULT_FONT, e);
                    defaultFont = null;
                }
            }
            return defaultFont;
        }

        /// <summary>
        /// Adds a
        /// <see cref="iText.Kernel.Font.PdfFont"/>
        /// instance to this document so that this font is flushed automatically
        /// on document close. As a side effect, the underlying font dictionary is made indirect if it wasn't the case yet
        /// </summary>
        /// <returns>the same PdfFont instance.</returns>
        public virtual PdfFont AddFont(PdfFont font) {
            font.MakeIndirect(this);
            documentFonts.Put(font.GetPdfObject().GetIndirectReference(), font);
            return font;
        }

        /// <summary>Gets list of indirect references.</summary>
        /// <returns>list of indirect references.</returns>
        internal virtual PdfXrefTable GetXref() {
            return xref;
        }

        /// <summary>
        /// Initialize
        /// <see cref="iText.Kernel.Pdf.Tagutils.TagStructureContext"/>
        /// .
        /// </summary>
        protected internal virtual void InitTagStructureContext() {
            tagStructureContext = new TagStructureContext(this);
        }

        /// <summary>Save the link annotation in a temporary storage for further copying.</summary>
        /// <param name="page">
        /// just copied
        /// <see cref="PdfPage"/>
        /// link annotation belongs to.
        /// </param>
        /// <param name="annotation">
        /// 
        /// <see cref="iText.Kernel.Pdf.Annot.PdfLinkAnnotation"/>
        /// itself.
        /// </param>
        protected internal virtual void StoreLinkAnnotation(PdfPage page, PdfLinkAnnotation annotation) {
            IList<PdfLinkAnnotation> pageAnnotations = linkAnnotations.Get(page);
            if (pageAnnotations == null) {
                pageAnnotations = new List<PdfLinkAnnotation>();
                linkAnnotations.Put(page, pageAnnotations);
            }
            pageAnnotations.Add(annotation);
        }

        /// <summary>Checks whether PDF document conforms a specific standard.</summary>
        /// <remarks>
        /// Checks whether PDF document conforms a specific standard.
        /// Shall be override.
        /// </remarks>
        protected internal virtual void CheckIsoConformance() {
        }

        /// <summary>
        /// Mark an object with
        /// <see cref="PdfObject.MUST_BE_FLUSHED"/>
        /// .
        /// </summary>
        /// <param name="pdfObject">an object to mark.</param>
        protected internal virtual void MarkObjectAsMustBeFlushed(PdfObject pdfObject) {
            if (pdfObject.IsIndirect()) {
                pdfObject.GetIndirectReference().SetState(PdfObject.MUST_BE_FLUSHED);
            }
        }

        /// <summary>Flush an object.</summary>
        /// <param name="pdfObject">object to flush.</param>
        /// <param name="canBeInObjStm">indicates whether object can be placed into object stream.</param>
        /// <exception cref="System.IO.IOException">on error.</exception>
        protected internal virtual void FlushObject(PdfObject pdfObject, bool canBeInObjStm) {
            writer.FlushObject(pdfObject, canBeInObjStm);
        }

        /// <summary>Initializes document.</summary>
        /// <param name="newPdfVersion">
        /// new pdf version of the resultant file if stamper is used and the version needs to be changed,
        /// or
        /// <see langword="null"/>
        /// otherwise
        /// </param>
        protected internal virtual void Open(PdfVersion newPdfVersion) {
            try {
                if (reader != null) {
                    reader.pdfDocument = this;
                    reader.ReadPdf();
                    Counter counter = GetCounter();
                    if (counter != null) {
                        counter.OnDocumentRead(reader.GetFileLength());
                    }
                    pdfVersion = reader.headerPdfVersion;
                    trailer = new PdfDictionary(reader.trailer);
                    PdfArray id = reader.trailer.GetAsArray(PdfName.ID);
                    if (id != null) {
                        originalModifiedDocumentId = id.GetAsString(1);
                    }
                    catalog = new PdfCatalog((PdfDictionary)trailer.Get(PdfName.Root, true));
                    if (catalog.GetPdfObject().ContainsKey(PdfName.Version)) {
                        // The version of the PDF specification to which the document conforms (for example, 1.4)
                        // if later than the version specified in the file's header
                        PdfVersion catalogVersion = PdfVersion.FromPdfName(catalog.GetPdfObject().GetAsName(PdfName.Version));
                        if (catalogVersion.CompareTo(pdfVersion) > 0) {
                            pdfVersion = catalogVersion;
                        }
                    }
                    PdfStream xmpMetadataStream = catalog.GetPdfObject().GetAsStream(PdfName.Metadata);
                    if (xmpMetadataStream != null) {
                        xmpMetadata = xmpMetadataStream.GetBytes();
                        try {
                            reader.pdfAConformanceLevel = PdfAConformanceLevel.GetConformanceLevel(XMPMetaFactory.ParseFromBuffer(xmpMetadata
                                ));
                        }
                        catch (XMPException) {
                        }
                    }
                    PdfObject infoDict = trailer.Get(PdfName.Info, true);
                    info = new PdfDocumentInfo(infoDict is PdfDictionary ? (PdfDictionary)infoDict : new PdfDictionary(), this
                        );
                    XmpMetaInfoConverter.AppendMetadataToInfo(xmpMetadata, info);
                    PdfDictionary str = catalog.GetPdfObject().GetAsDictionary(PdfName.StructTreeRoot);
                    if (str != null) {
                        TryInitTagStructure(str);
                    }
                    if (properties.appendMode && (reader.HasRebuiltXref() || reader.HasFixedXref())) {
                        throw new PdfException(PdfException.AppendModeRequiresADocumentWithoutErrorsEvenIfRecoveryWasPossible);
                    }
                }
                if (writer != null) {
                    if (reader != null && reader.HasXrefStm() && writer.properties.isFullCompression == null) {
                        writer.properties.isFullCompression = true;
                    }
                    if (reader != null && !reader.IsOpenedWithFullPermission()) {
                        throw new BadPasswordException(BadPasswordException.PdfReaderNotOpenedWithOwnerPassword);
                    }
                    if (reader != null && properties.preserveEncryption) {
                        writer.crypto = reader.decrypt;
                    }
                    writer.document = this;
                    String producer = null;
                    if (reader == null) {
                        catalog = new PdfCatalog(this);
                        info = new PdfDocumentInfo(this).AddCreationDate();
                        producer = iText.Kernel.Version.GetInstance().GetVersion();
                    }
                    else {
                        if (info.GetPdfObject().ContainsKey(PdfName.Producer)) {
                            producer = info.GetPdfObject().GetAsString(PdfName.Producer).ToUnicodeString();
                        }
                        producer = AddModifiedPostfix(producer);
                    }
                    info.AddModDate();
                    info.GetPdfObject().Put(PdfName.Producer, new PdfString(producer));
                    trailer = new PdfDictionary();
                    trailer.Put(PdfName.Root, catalog.GetPdfObject().GetIndirectReference());
                    trailer.Put(PdfName.Info, info.GetPdfObject().GetIndirectReference());
                    if (reader != null) {
                        // If the reader's trailer contains an ID entry, let's copy it over to the new trailer
                        if (reader.trailer.ContainsKey(PdfName.ID)) {
                            trailer.Put(PdfName.ID, reader.trailer.GetAsArray(PdfName.ID));
                        }
                    }
                }
                if (properties.appendMode) {
                    // Due to constructor reader and writer not null.
                    System.Diagnostics.Debug.Assert(reader != null);
                    RandomAccessFileOrArray file = reader.tokens.GetSafeFile();
                    int n;
                    byte[] buffer = new byte[8192];
                    while ((n = file.Read(buffer)) > 0) {
                        writer.Write(buffer, 0, n);
                    }
                    file.Close();
                    writer.Write((byte)'\n');
                    //TODO log if full compression differs
                    writer.properties.isFullCompression = reader.HasXrefStm();
                    writer.crypto = reader.decrypt;
                    if (newPdfVersion != null) {
                        // In PDF 1.4, a PDF version can also be specified in the Version entry of the document catalog,
                        // essentially updating the version associated with the file by overriding the one specified in the file header
                        if (pdfVersion.CompareTo(PdfVersion.PDF_1_4) >= 0) {
                            // If the header specifies a later version, or if this entry is absent, the document conforms to the
                            // version specified in the header.
                            // So only update the version if it is older than the one in the header
                            if (newPdfVersion.CompareTo(reader.headerPdfVersion) > 0) {
                                catalog.Put(PdfName.Version, newPdfVersion.ToPdfName());
                                catalog.SetModified();
                                pdfVersion = newPdfVersion;
                            }
                        }
                    }
                }
                else {
                    // Formally we cannot update version in the catalog as it is not supported for the
                    // PDF version of the original document
                    if (writer != null) {
                        if (newPdfVersion != null) {
                            pdfVersion = newPdfVersion;
                        }
                        writer.WriteHeader();
                        if (writer.crypto == null) {
                            writer.InitCryptoIfSpecified(pdfVersion);
                        }
                        if (writer.crypto != null) {
                            if (writer.crypto.GetCryptoMode() < EncryptionConstants.ENCRYPTION_AES_256) {
                                VersionConforming.ValidatePdfVersionForDeprecatedFeatureLogWarn(this, PdfVersion.PDF_2_0, VersionConforming
                                    .DEPRECATED_ENCRYPTION_ALGORITHMS);
                            }
                            else {
                                if (writer.crypto.GetCryptoMode() == EncryptionConstants.ENCRYPTION_AES_256) {
                                    PdfNumber r = writer.crypto.GetPdfObject().GetAsNumber(PdfName.R);
                                    if (r != null && r.IntValue() == 5) {
                                        VersionConforming.ValidatePdfVersionForDeprecatedFeatureLogWarn(this, PdfVersion.PDF_2_0, VersionConforming
                                            .DEPRECATED_AES256_REVISION);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (System.IO.IOException e) {
                throw new PdfException(PdfException.CannotOpenDocument, e, this);
            }
        }

        /// <summary>Adds custom XMP metadata extension.</summary>
        /// <remarks>Adds custom XMP metadata extension. Useful for PDF/UA, ZUGFeRD, etc.</remarks>
        /// <param name="xmpMeta">
        /// 
        /// <see cref="iText.Kernel.XMP.XMPMeta"/>
        /// to add custom metadata to.
        /// </param>
        protected internal virtual void AddCustomMetadataExtensions(XMPMeta xmpMeta) {
        }

        /// <summary>Updates XMP metadata.</summary>
        /// <remarks>
        /// Updates XMP metadata.
        /// Shall be override.
        /// </remarks>
        protected internal virtual void UpdateXmpMetadata() {
            try {
                // We add PDF producer info in any case, and the valid way to do it for PDF 2.0 in only in metadata, not in the info dictionary.
                if (writer.properties.addXmpMetadata || pdfVersion.CompareTo(PdfVersion.PDF_2_0) >= 0) {
                    SetXmpMetadata(UpdateDefaultXmpMetadata());
                }
            }
            catch (XMPException e) {
                ILogger logger = LoggerFactory.GetLogger(typeof(iText.Kernel.Pdf.PdfDocument));
                logger.Error(iText.IO.LogMessageConstant.EXCEPTION_WHILE_UPDATING_XMPMETADATA, e);
            }
        }

        /// <summary>
        /// Update XMP metadata values from
        /// <see cref="PdfDocumentInfo"/>
        /// .
        /// </summary>
        /// <exception cref="iText.Kernel.XMP.XMPException"/>
        protected internal virtual XMPMeta UpdateDefaultXmpMetadata() {
            XMPMeta xmpMeta = XMPMetaFactory.ParseFromBuffer(GetXmpMetadata(true));
            XmpMetaInfoConverter.AppendDocumentInfoToMetadata(info, xmpMeta);
            if (IsTagged() && !IsXmpMetaHasProperty(xmpMeta, XMPConst.NS_PDFUA_ID, XMPConst.PART)) {
                xmpMeta.SetPropertyInteger(XMPConst.NS_PDFUA_ID, XMPConst.PART, 1, new PropertyOptions(PropertyOptions.SEPARATE_NODE
                    ));
            }
            return xmpMeta;
        }

        /// <summary>List all newly added or loaded fonts</summary>
        /// <returns>
        /// List of
        /// <see cref="iText.Kernel.Font.PdfFont"/>
        /// .
        /// </returns>
        protected internal virtual ICollection<PdfFont> GetDocumentFonts() {
            return documentFonts.Values;
        }

        protected internal virtual void FlushFonts() {
            if (properties.appendMode) {
                foreach (PdfFont font in GetDocumentFonts()) {
                    if (font.GetPdfObject().CheckState(PdfObject.MUST_BE_INDIRECT) || font.GetPdfObject().GetIndirectReference
                        ().CheckState(PdfObject.MODIFIED)) {
                        font.Flush();
                    }
                }
            }
            else {
                foreach (PdfFont font in GetDocumentFonts()) {
                    font.Flush();
                }
            }
        }

        /// <summary>Checks page before adding and add.</summary>
        /// <param name="index">one-base index of the page.</param>
        /// <param name="page">
        /// 
        /// <see cref="PdfPage"/>
        /// to add.
        /// </param>
        protected internal virtual void CheckAndAddPage(int index, PdfPage page) {
            if (page.IsFlushed()) {
                throw new PdfException(PdfException.FlushedPageCannotBeAddedOrInserted, page);
            }
            if (page.GetDocument() != null && this != page.GetDocument()) {
                throw new PdfException(PdfException.Page1CannotBeAddedToDocument2BecauseItBelongsToDocument3).SetMessageParams
                    (page, this, page.GetDocument());
            }
            catalog.GetPageTree().AddPage(index, page);
        }

        /// <summary>Checks page before adding.</summary>
        /// <param name="page">
        /// 
        /// <see cref="PdfPage"/>
        /// to add.
        /// </param>
        protected internal virtual void CheckAndAddPage(PdfPage page) {
            if (page.IsFlushed()) {
                throw new PdfException(PdfException.FlushedPageCannotBeAddedOrInserted, page);
            }
            if (page.GetDocument() != null && this != page.GetDocument()) {
                throw new PdfException(PdfException.Page1CannotBeAddedToDocument2BecauseItBelongsToDocument3).SetMessageParams
                    (page, this, page.GetDocument());
            }
            catalog.GetPageTree().AddPage(page);
        }

        /// <summary>checks whether a method is invoked at the closed document</summary>
        protected internal virtual void CheckClosingStatus() {
            if (closed) {
                throw new PdfException(PdfException.DocumentClosedItIsImpossibleToExecuteAction);
            }
        }

        /// <summary>
        /// Gets
        /// <see cref="iText.Kernel.Log.Counter"/>
        /// instance.
        /// </summary>
        /// <returns>
        /// 
        /// <see cref="iText.Kernel.Log.Counter"/>
        /// instance.
        /// </returns>
        protected internal virtual Counter GetCounter() {
            return CounterFactory.GetCounter(typeof(iText.Kernel.Pdf.PdfDocument));
        }

        private void UpdateProducerInInfoDictionary() {
            String producer = null;
            if (reader == null) {
                producer = iText.Kernel.Version.GetInstance().GetVersion();
            }
            else {
                if (info.GetPdfObject().ContainsKey(PdfName.Producer)) {
                    producer = info.GetPdfObject().GetAsString(PdfName.Producer).ToUnicodeString();
                }
                producer = AddModifiedPostfix(producer);
            }
            info.GetPdfObject().Put(PdfName.Producer, new PdfString(producer));
        }

        private void TryInitTagStructure(PdfDictionary str) {
            try {
                structTreeRoot = new PdfStructTreeRoot(str, this);
                structParentIndex = GetStructTreeRoot().GetParentTreeNextKey();
            }
            catch (Exception ex) {
                structTreeRoot = null;
                structParentIndex = -1;
                ILogger logger = LoggerFactory.GetLogger(typeof(iText.Kernel.Pdf.PdfDocument));
                logger.Error(iText.IO.LogMessageConstant.TAG_STRUCTURE_INIT_FAILED, ex);
            }
        }

        private void TryFlushTagStructure(bool isAppendMode) {
            try {
                if (tagStructureContext != null) {
                    tagStructureContext.PrepareToDocumentClosing();
                }
                if (!isAppendMode || structTreeRoot.GetPdfObject().IsModified()) {
                    structTreeRoot.Flush();
                }
            }
            catch (Exception ex) {
                throw new PdfException(PdfException.TagStructureFlushingFailedItMightBeCorrupted, ex);
            }
        }

        private void UpdateValueInMarkInfoDict(PdfName key, PdfObject value) {
            PdfDictionary markInfo = catalog.GetPdfObject().GetAsDictionary(PdfName.MarkInfo);
            if (markInfo == null) {
                markInfo = new PdfDictionary();
                catalog.GetPdfObject().Put(PdfName.MarkInfo, markInfo);
            }
            markInfo.Put(key, value);
        }

        /// <summary>This method removes all annotation entries from form fields associated with a given page.</summary>
        /// <param name="page">to remove from.</param>
        private void RemoveUnusedWidgetsFromFields(PdfPage page) {
            if (page.IsFlushed()) {
                return;
            }
            IList<PdfAnnotation> annots = page.GetAnnotations();
            foreach (PdfAnnotation annot in annots) {
                if (annot.GetSubtype().Equals(PdfName.Widget)) {
                    ((PdfWidgetAnnotation)annot).ReleaseFormFieldFromWidgetAnnotation();
                }
            }
        }

        private void CopyLinkAnnotations(iText.Kernel.Pdf.PdfDocument toDocument, IDictionary<PdfPage, PdfPage> page2page
            ) {
            IList<PdfName> excludedKeys = new List<PdfName>();
            excludedKeys.Add(PdfName.Dest);
            excludedKeys.Add(PdfName.A);
            foreach (KeyValuePair<PdfPage, IList<PdfLinkAnnotation>> entry in linkAnnotations) {
                // We don't want to copy those link annotations, which reference to pages which weren't copied.
                foreach (PdfLinkAnnotation annot in entry.Value) {
                    bool toCopyAnnot = true;
                    PdfDestination copiedDest = null;
                    PdfDictionary copiedAction = null;
                    PdfObject dest = annot.GetDestinationObject();
                    if (dest != null) {
                        // If link annotation has destination object, we try to copy this destination.
                        // Destination is not copied if it points to the not copied page, and therefore the whole
                        // link annotation is not copied.
                        copiedDest = GetCatalog().CopyDestination(dest, page2page, toDocument);
                        toCopyAnnot = copiedDest != null;
                    }
                    else {
                        // Link annotation may have associated action. If it is GoTo type, we try to copy it's destination.
                        // GoToR and GoToE also contain destinations, but they point not to pages of the current document,
                        // so we just copy them as is. If it is action of any other type, it is also just copied as is.
                        PdfDictionary action = annot.GetAction();
                        if (action != null) {
                            if (PdfName.GoTo.Equals(action.Get(PdfName.S))) {
                                copiedAction = action.CopyTo(toDocument, iText.IO.Util.JavaUtil.ArraysAsList(PdfName.D), false);
                                PdfDestination goToDest = GetCatalog().CopyDestination(action.Get(PdfName.D), page2page, toDocument);
                                if (goToDest != null) {
                                    copiedAction.Put(PdfName.D, goToDest.GetPdfObject());
                                }
                                else {
                                    toCopyAnnot = false;
                                }
                            }
                            else {
                                copiedAction = ((PdfDictionary)action.CopyTo(toDocument, false));
                            }
                        }
                    }
                    if (toCopyAnnot) {
                        PdfLinkAnnotation newAnnot = (PdfLinkAnnotation)PdfAnnotation.MakeAnnotation(annot.GetPdfObject().CopyTo(toDocument
                            , excludedKeys, true));
                        if (copiedDest != null) {
                            newAnnot.SetDestination(copiedDest);
                        }
                        if (copiedAction != null) {
                            newAnnot.SetAction(copiedAction);
                        }
                        entry.Key.AddAnnotation(-1, newAnnot, false);
                    }
                }
            }
            linkAnnotations.Clear();
        }

        /// <summary>This method copies all given outlines</summary>
        /// <param name="outlines">outlines to be copied</param>
        /// <param name="toDocument">document where outlines should be copied</param>
        private void CopyOutlines(ICollection<PdfOutline> outlines, iText.Kernel.Pdf.PdfDocument toDocument, IDictionary
            <PdfPage, PdfPage> page2page) {
            ICollection<PdfOutline> outlinesToCopy = new HashSet<PdfOutline>();
            outlinesToCopy.AddAll(outlines);
            foreach (PdfOutline outline in outlines) {
                GetAllOutlinesToCopy(outline, outlinesToCopy);
            }
            PdfOutline rootOutline = toDocument.GetOutlines(false);
            if (rootOutline == null) {
                rootOutline = new PdfOutline(toDocument);
                rootOutline.SetTitle("Outlines");
            }
            CloneOutlines(outlinesToCopy, rootOutline, GetOutlines(false), page2page, toDocument);
        }

        /// <summary>This method gets all outlines to be copied including parent outlines</summary>
        /// <param name="outline">current outline</param>
        /// <param name="outlinesToCopy">a Set of outlines to be copied</param>
        private void GetAllOutlinesToCopy(PdfOutline outline, ICollection<PdfOutline> outlinesToCopy) {
            PdfOutline parent = outline.GetParent();
            //note there's no need to continue recursion if the current outline parent is root (first condition) or
            // if it is already in the Set of outlines to be copied (second condition)
            if (parent.GetTitle().Equals("Outlines") || outlinesToCopy.Contains(parent)) {
                return;
            }
            outlinesToCopy.Add(parent);
            GetAllOutlinesToCopy(parent, outlinesToCopy);
        }

        /// <summary>This method copies create new outlines in the Document to copy.</summary>
        /// <param name="outlinesToCopy">- Set of outlines to be copied</param>
        /// <param name="newParent">- new parent outline</param>
        /// <param name="oldParent">- old parent outline</param>
        private void CloneOutlines(ICollection<PdfOutline> outlinesToCopy, PdfOutline newParent, PdfOutline oldParent
            , IDictionary<PdfPage, PdfPage> page2page, iText.Kernel.Pdf.PdfDocument toDocument) {
            if (null == oldParent) {
                return;
            }
            foreach (PdfOutline outline in oldParent.GetAllChildren()) {
                if (outlinesToCopy.Contains(outline)) {
                    PdfObject destObjToCopy = outline.GetDestination().GetPdfObject();
                    PdfDestination copiedDest = GetCatalog().CopyDestination(destObjToCopy, page2page, toDocument);
                    PdfOutline child = newParent.AddOutline(outline.GetTitle());
                    if (copiedDest != null) {
                        child.AddDestination(copiedDest);
                    }
                    CloneOutlines(outlinesToCopy, child, outline, page2page, toDocument);
                }
            }
        }

        private void EnsureTreeRootAddedToNames(PdfObject treeRoot, PdfName treeType) {
            PdfDictionary names = catalog.GetPdfObject().GetAsDictionary(PdfName.Names);
            if (names == null) {
                names = new PdfDictionary();
                catalog.GetPdfObject().Put(PdfName.Names, names);
                names.MakeIndirect(this);
            }
            names.Put(treeType, treeRoot);
        }

        /// <exception cref="iText.Kernel.XMP.XMPException"/>
        private static bool IsXmpMetaHasProperty(XMPMeta xmpMeta, String schemaNS, String propName) {
            return xmpMeta.GetProperty(schemaNS, propName) != null;
        }

        private long GetDocumentId() {
            return documentId;
        }

        /// <summary>A structure storing documentId, object number and generation number.</summary>
        /// <remarks>
        /// A structure storing documentId, object number and generation number. This structure is using to calculate
        /// an unique object key during the copy process.
        /// </remarks>
        internal class IndirectRefDescription {
            internal readonly long docId;

            internal readonly int objNr;

            internal readonly int genNr;

            internal IndirectRefDescription(PdfIndirectReference reference) {
                this.docId = reference.GetDocument().GetDocumentId();
                this.objNr = reference.GetObjNumber();
                this.genNr = reference.GetGenNumber();
            }

            public override int GetHashCode() {
                int result = (int)docId;
                result *= 31;
                result += objNr;
                result *= 31;
                result += genNr;
                return result;
            }

            public override bool Equals(Object o) {
                if (this == o) {
                    return true;
                }
                if (o == null || GetType() != o.GetType()) {
                    return false;
                }
                PdfDocument.IndirectRefDescription that = (PdfDocument.IndirectRefDescription)o;
                return docId == that.docId && objNr == that.objNr && genNr == that.genNr;
            }
        }

        private String AddModifiedPostfix(String producer) {
            iText.Kernel.Version version = iText.Kernel.Version.GetInstance();
            if (producer == null || !version.GetVersion().Contains(version.GetProduct())) {
                return version.GetVersion();
            }
            else {
                int idx = producer.IndexOf("; modified using", StringComparison.Ordinal);
                StringBuilder buf;
                if (idx == -1) {
                    buf = new StringBuilder(producer);
                }
                else {
                    buf = new StringBuilder(producer.JSubstring(0, idx));
                }
                buf.Append("; modified using ");
                buf.Append(version.GetVersion());
                return buf.ToString();
            }
        }

        void System.IDisposable.Dispose() {
            Close();
        }
    }
}

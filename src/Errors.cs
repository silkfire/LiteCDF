namespace LiteCDF
{
    internal static class Errors
    {
        private const string CorruptDocumentIndication = "This is an indication that the compound document is invalid or corrupt.";


        public const string FileDoesNotExist = "File '{0}' does not exist.";
        public const string EmptyDataStream = "The provided data stream cannot be empty.";
        public const string StreamNamePredicateNull = "The provided stream name predicate cannot be null.";
        public const string HeaderSignatureMissing = "Invalid compound document, signature missing in header.";
        public const string SectorSizeTooSmall = "Standard sector size too small. " + CorruptDocumentIndication;
        public const string ShortSectorSizeGreaterThanStandardSectorSize = "Short-sector size cannot exceed standard sector size. " + CorruptDocumentIndication;
        public const string InvalidSecIdReference = "Sector ID references a non-existent sector. " + CorruptDocumentIndication;
        public const string EmptySatSecIdChain = "SAT sector ID chain is empty. " + CorruptDocumentIndication;
        public const string CyclicSecIdChain = "Cyclic sector ID chain detected while establishing length of the directory stream. " + CorruptDocumentIndication;
        public const string FirstDirectoryEntryMustBeRootStorage = "First directory entry must be the root storage. " + CorruptDocumentIndication;
        public const string NoShortStreamContainerStreamDefined = "Stream requires the short-stream container stream to be read, which was not defined in the document. " + CorruptDocumentIndication;
        public const string ShortStreamContainerStreamSizeIsZero = "Size of the short-stream container stream cannot be zero. " + CorruptDocumentIndication;
        public const string UnexpectedEndOfStream = "End of data stream reached prematurely. " + CorruptDocumentIndication;
        public const string DirectoryEntryNameTooLong = "Name of directory entry exceeds 31 characters. " + CorruptDocumentIndication;
    }
}

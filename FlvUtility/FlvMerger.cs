namespace FlvUtility
{
    public class FlvMerger
    {
        const int FLV_HEADER_SIZE = 9;
        const int FLV_TAG_HEADER_SIZE = 11;
        const int MAX_DATA_SIZE = 16777220;

        public string TargetPath { get; private set; } = "";
        public string LastMergedFile { get; private set; } = "";
        public int CurrentTimeline { get; private set; } = 0;
        FileStream targetFs;

        public FlvMerger(string targetFile)
        {
            TargetPath = targetFile;
            targetFs = new FileStream(TargetPath, FileMode.Create);
        }

        public async Task AppendAsync(string sourceFile)
        {
            if(!File.Exists(sourceFile))
            {
                throw new FileNotFoundException("File not found", sourceFile);
            }
            if (File.Exists(LastMergedFile))
            {
                if(!IsSuitableToMerge(await GetFLVFileInfo(LastMergedFile), await GetFLVFileInfo(sourceFile)))
                {
                    throw new InvalidOperationException("Video or audio track encoding won't fit.");
                }
            }
            using (FileStream sourceFs = new FileStream(sourceFile, FileMode.Open))
            {
                CurrentTimeline = await Merge(sourceFs, targetFs, CurrentTimeline == 0, CurrentTimeline);
            }
            LastMergedFile = sourceFile;
        }

        public async Task Close()
        {
            await targetFs.FlushAsync();
            targetFs.Close();
        }

        class FLVContext
        {
            public byte soundFormat;
            public byte soundRate;
            public byte soundSize;
            public byte soundType;
            public byte videoCodecID;
        }

        bool IsSuitableToMerge(FLVContext flvCtx1, FLVContext flvCtx2)
        {
            return (flvCtx1.soundFormat == flvCtx2.soundFormat) &&
              (flvCtx1.soundRate == flvCtx2.soundRate) &&
              (flvCtx1.soundSize == flvCtx2.soundSize) &&
              (flvCtx1.soundType == flvCtx2.soundType) &&
              (flvCtx1.videoCodecID == flvCtx2.videoCodecID);
        }

        bool IsFLVFile(FileStream fs)
        {
            int len;
            byte[] buf = new byte[FLV_HEADER_SIZE];
            fs.Position = 0;
            if (FLV_HEADER_SIZE != fs.Read(buf, 0, buf.Length))
                return false;

            if (buf[0] != 'F' || buf[1] != 'L' || buf[2] != 'V' || buf[3] != 0x01)
                return false;
            else
                return true;
        }

        async Task<FLVContext> GetFLVFileInfo(string fname)
        {
            using(FileStream fs = new FileStream(fname, FileMode.Open))
            {
                return await GetFLVFileInfo(fs);
            }
        }

        async Task<FLVContext> GetFLVFileInfo(FileStream fs)
        {
            bool hasAudioParams, hasVideoParams;
            int skipSize, readLen;
            int dataSize;
            byte tagType;
            byte[] tmp = new byte[FLV_TAG_HEADER_SIZE + 1];
            if (fs == null) return null;

            FLVContext flvCtx = new FLVContext();
            fs.Position = 0;
            skipSize = 9;
            fs.Position += skipSize;
            hasVideoParams = hasAudioParams = false;
            skipSize = 4;
            while (!hasVideoParams || !hasAudioParams)
            {
                fs.Position += skipSize;

                if (FLV_TAG_HEADER_SIZE + 1 != await fs.ReadAsync(tmp, 0, tmp.Length))
                    return null;

                tagType = (byte)(tmp[0] & 0x1f);
                switch (tagType)
                {
                    case 8:
                        flvCtx.soundFormat = (byte)((tmp[FLV_TAG_HEADER_SIZE] & 0xf0) >> 4);
                        flvCtx.soundRate = (byte)((tmp[FLV_TAG_HEADER_SIZE] & 0x0c) >> 2);
                        flvCtx.soundSize = (byte)((tmp[FLV_TAG_HEADER_SIZE] & 0x02) >> 1);
                        flvCtx.soundType = (byte)((tmp[FLV_TAG_HEADER_SIZE] & 0x01) >> 0);
                        hasAudioParams = true;
                        break;
                    case 9:
                        flvCtx.videoCodecID = (byte)((tmp[FLV_TAG_HEADER_SIZE] & 0x0f));
                        hasVideoParams = true;
                        break;
                    default:
                        break;
                }

                dataSize = FromInt24StringBe(tmp[1], tmp[2], tmp[3]);
                skipSize = dataSize - 1 + 4;
            }

            return flvCtx;
        }

        int FromInt24StringBe(byte b0, byte b1, byte b2)
        {
            return (int)((b0 << 16) | (b1 << 8) | (b2));
        }

        int GetTimestamp(byte b0, byte b1, byte b2, byte b3)
        {
            return ((b3 << 24) | (b0 << 16) | (b1 << 8) | (b2));
        }

        void SetTimestamp(byte[] data, int idx, int newTimestamp)
        {
            data[idx + 3] = (byte)(newTimestamp >> 24);
            data[idx + 0] = (byte)(newTimestamp >> 16);
            data[idx + 1] = (byte)(newTimestamp >> 8);
            data[idx + 2] = (byte)(newTimestamp);
        }

        async Task<int> Merge(FileStream fsInput, FileStream fsMerge, bool isFirstFile, int lastTimestamp = 0)
        {
            int readLen;
            int curTimestamp = 0;
            int newTimestamp = 0;
            int dataSize;
            byte[] tmp = new byte[20];
            byte[] buf = new byte[MAX_DATA_SIZE];

            fsInput.Position = 0;
            if (isFirstFile)
            {
                if (FLV_HEADER_SIZE + 4 == (await fsInput.ReadAsync(tmp, 0, FLV_HEADER_SIZE + 4)))
                {
                    fsMerge.Position = 0;
                    await fsMerge.WriteAsync(tmp, 0, FLV_HEADER_SIZE + 4);
                }
            }
            else
            {
                fsInput.Position = FLV_HEADER_SIZE + 4;
            }

            while (await fsInput.ReadAsync(tmp, 0, FLV_TAG_HEADER_SIZE) > 0)
            {
                dataSize = FromInt24StringBe(tmp[1], tmp[2], tmp[3]);
                curTimestamp = GetTimestamp(tmp[4], tmp[5], tmp[6], tmp[7]);
                newTimestamp = curTimestamp + lastTimestamp;
                SetTimestamp(tmp, 4, newTimestamp);
                await fsMerge.WriteAsync(tmp, 0, FLV_TAG_HEADER_SIZE);

                readLen = dataSize + 4;
                if (await fsInput.ReadAsync(buf, 0, readLen) > 0)
                {
                    await fsMerge.WriteAsync(buf, 0, readLen);
                }
                else
                {
                    goto failed;
                }
            }

            return newTimestamp;

        failed:
            throw new Exception("Merge Failed");
        }
    }
}

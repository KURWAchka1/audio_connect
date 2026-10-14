package io.github.kurwachka1.audioconnect;

import org.junit.Test;
import static org.junit.Assert.*;
import java.io.*;
import java.nio.*;

public class AudioProtocolTest {
    private byte[] header() { return ByteBuffer.allocate(20).order(ByteOrder.LITTLE_ENDIAN).put(new byte[]{'A','C','F','1'}).putInt(48000).putShort((short) 2).putShort((short) 16).putInt(480).putInt(0).array(); }
    private byte[] packet(int sequence, int length) { return ByteBuffer.allocate(8 + 1920).order(ByteOrder.LITTLE_ENDIAN).putInt(sequence).putInt(length).array(); }
    @Test public void fragmentedHeaderIsReadExactly() throws Exception {
        InputStream input = new ByteArrayInputStream(header()) { @Override public synchronized int read(byte[] b, int off, int len) { return super.read(b, off, Math.min(1, len)); } };
        AudioProtocol.readHeader(input);
    }
    @Test(expected = IOException.class) public void incompatibleHeaderIsRejected() throws Exception { byte[] value = header(); value[4] = 0; AudioProtocol.readHeader(new ByteArrayInputStream(value)); }
    @Test(expected = EOFException.class) public void truncatedPayloadIsRejected() throws Exception { new AudioProtocol.Reader().read(new ByteArrayInputStream(new byte[4]), new byte[1920]); }
    @Test(expected = IOException.class) public void maliciousLengthIsRejected() throws Exception { new AudioProtocol.Reader().read(new ByteArrayInputStream(packet(0, Integer.MAX_VALUE)), new byte[1920]); }
    @Test public void sequenceWrapIsValid() throws Exception { AudioProtocol.Reader reader = new AudioProtocol.Reader(); byte[] pcm = new byte[1920]; assertEquals(0xffffffffL, reader.read(new ByteArrayInputStream(packet(-1, 1920)), pcm)); assertEquals(0, reader.read(new ByteArrayInputStream(packet(0, 1920)), pcm)); }
    @Test(expected = IOException.class) public void duplicatedPacketIsRejected() throws Exception { AudioProtocol.Reader reader = new AudioProtocol.Reader(); byte[] pcm = new byte[1920]; reader.read(new ByteArrayInputStream(packet(5, 1920)), pcm); reader.read(new ByteArrayInputStream(packet(5, 1920)), pcm); }
    @Test public void helloHasExpectedWireLayout() throws Exception { byte[] hello = AudioProtocol.hello("ab".repeat(32)); assertEquals(36, hello.length); assertEquals('A', hello[0]); assertEquals((byte) 0xab, hello[35]); }
    @Test(expected = IOException.class) public void invalidTokenIsRejected() throws Exception { AudioProtocol.hello("../../invalid"); }
}

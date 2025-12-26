using System;

namespace PreFusion.Core.Compression
{
    public static class MiniLZO
    {
        public static byte[] Decompress(byte[] src, int srcOff, int srcLen, int dstLen)
        {
            if (srcLen == 0 && dstLen == 0) return new byte[0];
            
            byte[] dst = new byte[dstLen];
            int ip = srcOff;
            int op = 0;
            int ip_end = srcOff + srcLen;
            // int op_end = dstLen; // Not strictly used for bounds locally

            if (ip >= ip_end) return dst;

            // Header Check
            if (src[ip] > 17)
            {
                int t = src[ip++] - 17;
                if (t < 4)
                {
                    // match_next logic
                    do
                    {
                        dst[op++] = src[ip++];
                        dst[op++] = src[ip++];
                        dst[op++] = src[ip++];
                        dst[op++] = src[ip++];
                        t -= 4;
                    } while (t > 0);
                    
                    t = src[ip++] - 17;
                }
                else
                {
                    // Copy literals
                    dst[op++] = src[ip++]; dst[op++] = src[ip++]; dst[op++] = src[ip++]; dst[op++] = src[ip++];
                    t -= 4;
                    if (t > 0)
                    {
                        do { dst[op++] = src[ip++]; } while (--t > 0);
                    }
                }

                // Fallthrough to 'first_literal_run' logic (Duplicated here to avoid goto into loop)
                // This block handles the state after the initial special case
                {
                     int m_pos_jump = 0;
                     // Logic from first_literal_run
                     if (t >= 64)
                     {
                        m_pos_jump = op - 1 - ((t >> 2) & 7) - (src[ip++] << 3);
                        t = (t >> 5) - 1;
                     }
                     else if (t >= 32)
                     {
                        t &= 31;
                        if (t == 0)
                        {
                             while (src[ip] == 0) { t += 255; ip++; }
                             t += 31 + src[ip++];
                        }
                        m_pos_jump = op - 1 - (src[ip] >> 2) - (src[ip + 1] << 6);
                        ip += 2;
                     }
                     else if (t >= 16)
                     {
                        m_pos_jump = op - ((t & 8) << 11);
                        t &= 7;
                        if (t == 0)
                        {
                            while (src[ip] == 0) { t += 255; ip++; }
                            t += 7 + src[ip++];
                        }
                        m_pos_jump -= (src[ip] >> 2) + (src[ip + 1] << 6);
                        ip += 2;
                        if (m_pos_jump == op) return dst; // EOF condition
                        m_pos_jump -= 0x4000;
                     }
                     else 
                     {
                        // Should not happen in this specific fallthrough path given known LZO headers?
                        // But if it does, it's a direct match?
                        // Handle as "fallback" for safety
                         m_pos_jump = op - 1 - (t >> 2) - (src[ip++] << 2);
                         dst[op++] = dst[m_pos_jump++]; dst[op++] = dst[m_pos_jump++];
                         goto match_done_init;
                     }

                     // copy_match
                     if (t >= 0) // t is basically match_len - 2
                     {
                        dst[op++] = dst[m_pos_jump++]; dst[op++] = dst[m_pos_jump++];
                        do { dst[op++] = dst[m_pos_jump++]; } while (--t > 0);
                     }
                     
                     match_done_init:
                     // Trailing literals for init block
                     t = src[ip++] & 3;
                     if (t > 0)
                     {
                         dst[op++] = src[ip++];
                         if (t > 1) dst[op++] = src[ip++];
                     }
                }
            }

            // Main Loop
            while (true)
            {
                if (ip >= ip_end) break;
                
                int t = src[ip++]; // match_start
                
                if (t < 16)
                {
                    if (t == 0)
                    {
                        while (src[ip] == 0)
                        {
                            t += 255;
                            ip++;
                            if (ip >= ip_end) throw new IndexOutOfRangeException("Input overrun");
                        }
                        t += 15 + src[ip++];
                    }
                    t += 3;
                    
                    // Copy literals
                    for (int i = 0; i < t; i++) dst[op++] = src[ip++];
                    
                    t = src[ip++];
                    
                    if (t < 16)
                    {
                        // Match
                        int m_pos = op - 1 - (t >> 2) - (src[ip++] << 2);
                        dst[op++] = dst[m_pos++]; dst[op++] = dst[m_pos++];
                        goto match_done;
                    }
                }
                
                // first_literal_run logic
                {
                     int m_pos_jump = 0;
                     if (t >= 64)
                     {
                        m_pos_jump = op - 1 - ((t >> 2) & 7) - (src[ip++] << 3);
                        t = (t >> 5) - 1;
                     }
                     else if (t >= 32)
                     {
                        t &= 31;
                        if (t == 0)
                        {
                             while (src[ip] == 0) { t += 255; ip++; }
                             t += 31 + src[ip++];
                        }
                        m_pos_jump = op - 1 - (src[ip] >> 2) - (src[ip + 1] << 6);
                        ip += 2;
                     }
                     else if (t >= 16)
                     {
                        m_pos_jump = op - ((t & 8) << 11);
                        t &= 7;
                        if (t == 0)
                        {
                            while (src[ip] == 0) { t += 255; ip++; }
                            t += 7 + src[ip++];
                        }
                        m_pos_jump -= (src[ip] >> 2) + (src[ip + 1] << 6);
                        ip += 2;
                        if (m_pos_jump == op) break; // EOF
                        m_pos_jump -= 0x4000;
                     }
                     else
                     {
                         // t < 16 treated above? No, t can be >= 16 here.
                         // Wait, if t < 16 we entered the if block above.
                         // But inside that block, we read NEW t. If NEW t < 16 we goto match_done.
                         // If we are HERE, t must be >= 16? 
                         // No, in the logic flow: 
                         // if (t < 16) { ... read literals ... t = src[ip++]; if (t < 16) goto match_done; }
                         // If we are here, either:
                         // 1. Initial t >= 16.
                         // 2. Initial t < 16, literals copied, read new t, and NEW t >= 16.
                         // So we are consistent.
                         
                         m_pos_jump = op - 1 - (t >> 2) - (src[ip++] << 2);
                         dst[op++] = dst[m_pos_jump++]; dst[op++] = dst[m_pos_jump++];
                         goto match_done;
                     }

                     // copy_match
                     if (t >= 0)
                     {
                        dst[op++] = dst[m_pos_jump++]; dst[op++] = dst[m_pos_jump++]; // first 2
                        do { dst[op++] = dst[m_pos_jump++]; } while (--t > 0);
                     }
                }

                match_done:
                // Trailing literals
                t = src[ip++] & 3;
                if (t > 0)
                {
                    dst[op++] = src[ip++];
                    if (t > 1) dst[op++] = src[ip++];
                }
            }

            return dst;
        }
    }
}

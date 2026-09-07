using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MuseDeskNative
{
    internal static class TextEncoding
    {
        internal static string ReadFile(string path){return Decode(File.ReadAllBytes(path),null,false);}
        internal static string Decode(byte[] bytes,string declared=null,bool console=false)
        {
            if(bytes.Length==0)return "";
            if(bytes.Length>=4 && bytes[0]==255 && bytes[1]==254 && bytes[2]==0 && bytes[3]==0)return new UTF32Encoding(false,true,true).GetString(bytes,4,bytes.Length-4);
            if(bytes.Length>=4 && bytes[0]==0 && bytes[1]==0 && bytes[2]==254 && bytes[3]==255)return new UTF32Encoding(true,true,true).GetString(bytes,4,bytes.Length-4);
            if(bytes.Length>=3 && bytes[0]==239 && bytes[1]==187 && bytes[2]==191)return new UTF8Encoding(false,true).GetString(bytes,3,bytes.Length-3);
            if(bytes.Length>=2 && bytes[0]==255 && bytes[1]==254)return new UnicodeEncoding(false,true,true).GetString(bytes,2,bytes.Length-2);
            if(bytes.Length>=2 && bytes[0]==254 && bytes[1]==255)return new UnicodeEncoding(true,true,true).GetString(bytes,2,bytes.Length-2);
            if(!string.IsNullOrWhiteSpace(declared))
            {
                try{return Encoding.GetEncoding(declared.Trim('"','\'', ' '),EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback).GetString(bytes);}
                catch(ArgumentException){}
            }
            // Windows PowerShell and cmd /u can write BOM-less UTF-16 to a pipe.
            if(bytes.Length%2==0 && bytes.Length>=4)
            {
                int low=0,high=0;for(int i=0;i<bytes.Length;i+=2){if(bytes[i]==0 || bytes[i]==4)low++;if(bytes[i+1]==0 || bytes[i+1]==4)high++;}
                if(high>bytes.Length/3 && high>low*2)return new UnicodeEncoding(false,false,true).GetString(bytes);
                if(low>bytes.Length/3 && low>high*2)return new UnicodeEncoding(true,false,true).GetString(bytes);
            }
            try{return new UTF8Encoding(false,true).GetString(bytes);}catch(DecoderFallbackException){}
            string ansi=Encoding.GetEncoding(1251).GetString(bytes),oem=Encoding.GetEncoding(866).GetString(bytes);
            double a=RussianScore(ansi),b=RussianScore(oem);
            return b>a || (console && Math.Abs(a-b)<.1)?oem:ansi;
        }
        private static double RussianScore(string text)
        {
            double score=0;
            foreach(char c in text)
            {
                if(c>='а'&&c<='я')score+=2;
                else if(c>='А'&&c<='Я')score+=1;
                else if(c=='ё'||c=='Ё')score+=2;
                else if(c>=0x2500&&c<=0x259f)score-=6;
                else if(c>=0x400&&c<=0x4ff)score-=2;
                else if(char.IsControl(c)&&c!='\r'&&c!='\n'&&c!='\t')score-=8;
            }
            string lower=text.ToLowerInvariant();foreach(string pair in new[]{"ст","но","то","на","ен","ов","ни","ра","во","ко","пр","ро","по","ос","ал","ли","ер","го","ре","ет","ть"})score+=Regex.Matches(lower,pair).Count*2;
            return score;
        }
        internal static string DecodeHtml(byte[] bytes,string declared)
        {
            if(string.IsNullOrWhiteSpace(declared))
            {
                string header=Encoding.ASCII.GetString(bytes,0,Math.Min(8192,bytes.Length));
                Match charset=Regex.Match(header,"<meta[^>]+charset\\s*=\\s*['\"]?([A-Za-z0-9_-]+)",RegexOptions.IgnoreCase);
                if(charset.Success)declared=charset.Groups[1].Value;
            }
            return Decode(bytes,declared,false);
        }
    }
}

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
#include <algorithm>
#include <regex>
#include <iostream>

using namespace std;

const string ALPHABET = "BCDFGHJKMPQRTVWXY2346789";
uint32_t CRC32_TABLE[256];

void initCrc(){
    for(int i=0;i<256;i++){
        uint32_t k = (uint32_t)i << 24;
        for(int bit=0; bit<8; bit++){
            if(k & 0x80000000u) k = (k<<1) ^ 0x4c11db7u;
            else k = k<<1;
        }
        CRC32_TABLE[i] = k & 0xffffffffu;
    }
}

struct BigInt {
    vector<uint32_t> limbs; // little-endian, base 2^32

    BigInt() {}
    BigInt(uint64_t v){
        if(v){ limbs.push_back((uint32_t)v); if(v>>32) limbs.push_back((uint32_t)(v>>32)); }
    }

    void trim(){ while(!limbs.empty() && limbs.back()==0) limbs.pop_back(); }
    bool isZero() const { return limbs.empty(); }

    static int hexVal(char c){
        if(c>='0'&&c<='9') return c-'0';
        if(c>='a'&&c<='f') return c-'a'+10;
        if(c>='A'&&c<='F') return c-'A'+10;
        return 0;
    }

    static BigInt fromHex(const string& hex){
        BigInt r;
        for(char c: hex){
            r.mulSmall(16);
            r.addSmall((uint64_t)hexVal(c));
        }
        return r;
    }

    void mulSmall(uint64_t m){
        uint64_t carry=0;
        for(auto &l: limbs){
            uint64_t v = (uint64_t)l*m + carry;
            l = (uint32_t)v;
            carry = v>>32;
        }
        while(carry){ limbs.push_back((uint32_t)carry); carry>>=32; }
        trim();
    }

    void addSmall(uint64_t a){
        size_t i=0;
        while(a){
            if(i>=limbs.size()) limbs.push_back(0);
            uint64_t v = (uint64_t)limbs[i] + (a & 0xffffffffULL);
            limbs[i] = (uint32_t)v;
            a = (a>>32) + (v>>32);
            i++;
        }
        trim();
    }

    uint64_t divmodSmall(uint64_t d){
        uint64_t rem=0;
        for(int i=(int)limbs.size()-1;i>=0;i--){
            uint64_t cur = (rem<<32) | (uint64_t)limbs[i];
            limbs[i] = (uint32_t)(cur/d);
            rem = cur % d;
        }
        trim();
        return rem;
    }

    void shl(int n){
        if(isZero()||n==0) return;
        int limbShift = n/32, bitShift = n%32;
        vector<uint32_t> res(limbs.size()+limbShift+1,0);
        for(size_t i=0;i<limbs.size();i++){
            uint64_t v = (uint64_t)limbs[i] << bitShift;
            res[i+limbShift] |= (uint32_t)v;
            res[i+limbShift+1] |= (uint32_t)(v>>32);
        }
        limbs = res; trim();
    }

    void shr(int n){
        if(n==0) return;
        int limbShift = n/32, bitShift = n%32;
        if((size_t)limbShift >= limbs.size()){ limbs.clear(); return; }
        vector<uint32_t> res(limbs.size()-limbShift,0);
        for(size_t i=0;i<res.size();i++){
            uint64_t lo = (uint64_t)limbs[i+limbShift] >> bitShift;
            uint64_t hi = 0;
            if(bitShift>0 && i+limbShift+1<limbs.size())
                hi = (uint64_t)limbs[i+limbShift+1] << (32-bitShift);
            res[i] = (uint32_t)(lo|hi);
        }
        limbs = res; trim();
    }

    BigInt bitAnd(const BigInt& o) const{
        BigInt r; size_t n=min(limbs.size(),o.limbs.size());
        r.limbs.resize(n);
        for(size_t i=0;i<n;i++) r.limbs[i]=limbs[i]&o.limbs[i];
        r.trim(); return r;
    }

    int cmp(const BigInt& o) const{
        if(limbs.size()!=o.limbs.size()) return limbs.size()<o.limbs.size()?-1:1;
        for(int i=(int)limbs.size()-1;i>=0;i--)
            if(limbs[i]!=o.limbs[i]) return limbs[i]<o.limbs[i]?-1:1;
        return 0;
    }

    uint64_t toUint64() const{
        uint64_t r=0;
        if(limbs.size()>0) r = limbs[0];
        if(limbs.size()>1) r |= (uint64_t)limbs[1]<<32;
        return r;
    }

    vector<uint8_t> bytes() const{
        vector<uint8_t> r;
        for(int i=(int)limbs.size()-1;i>=0;i--){
            for(int b=3;b>=0;b--){
                uint8_t byte = (limbs[i]>>(b*8))&0xff;
                if(!r.empty()||byte!=0) r.push_back(byte);
            }
        }
        return r;
    }

    string toHexPadded(int width) const{
        string s;
        for(int i=(int)limbs.size()-1;i>=0;i--){
            char buf[9];
            snprintf(buf,sizeof(buf),"%08x",limbs[i]);
            s+=buf;
        }
        if(s.empty()) s="0";
        while((int)s.size()<width) s="0"+s;
        return s;
    }

    string toHex() const{
        if(isZero()) return "0";
        string s;
        for(int i=(int)limbs.size()-1;i>=0;i--){
            char buf[9];
            if(i==(int)limbs.size()-1) snprintf(buf,sizeof(buf),"%x",limbs[i]);
            else snprintf(buf,sizeof(buf),"%08x",limbs[i]);
            s+=buf;
        }
        return s;
    }

    string toDec() const{
        if(isZero()) return "0";
        BigInt tmp = *this;
        string s;
        while(!tmp.isZero()){
            uint64_t r = tmp.divmodSmall(10);
            s += char('0'+r);
        }
        reverse(s.begin(), s.end());
        return s;
    }
};

// ---------------- Product key decode/encode ----------------
struct ProductKeyDecoder {
    string Key5x5;
    BigInt Key;
    uint64_t Group, Serial, Security, Checksum, Upgrade, Extra;
};

BigInt decode5x5(string key){
    string k;
    for(char c: key) if(c!='-') k+=c;

    int nPos = (int)k.find('N');

    string kNoN;
    for(char c: k) if(c!='N') kNoN+=c;

    vector<int> dec;
    dec.push_back(nPos);
    for(char c: kNoN) dec.push_back((int)ALPHABET.find(c));

    BigInt result;
    for(int x: dec){
        result.mulSmall(24);
        result.addSmall((uint64_t)x);
    }
    return result;
}

ProductKeyDecoder NewProductKeyDecoder(const string& key){
    BigInt keyInt = decode5x5(key);

    BigInt mask1 = BigInt::fromHex("fffff");
    BigInt mask2 = BigInt::fromHex("3fffffff00000");
    BigInt mask3 = BigInt::fromHex("1fffffffffffff000000000000");
    BigInt mask4 = BigInt::fromHex("3ff0000000000000000000000000");
    BigInt mask5 = BigInt::fromHex("20000000000000000000000000000");
    BigInt mask6 = BigInt::fromHex("40000000000000000000000000000");

    BigInt group = keyInt.bitAnd(mask1);

    BigInt serial = keyInt.bitAnd(mask2); serial.shr(20);
    BigInt security = keyInt.bitAnd(mask3); security.shr(50);
    BigInt checksum = keyInt.bitAnd(mask4); checksum.shr(103);
    BigInt upgrade = keyInt.bitAnd(mask5); upgrade.shr(113);
    BigInt extra = keyInt.bitAnd(mask6); extra.shr(114);

    ProductKeyDecoder d;
    d.Key5x5 = key;
    d.Key = keyInt;
    d.Group = group.toUint64();
    d.Serial = serial.toUint64();
    d.Security = security.toUint64();
    d.Checksum = checksum.toUint64();
    d.Upgrade = upgrade.toUint64();
    d.Extra = extra.toUint64();
    return d;
}

vector<uint8_t> encodeBytes(BigInt key){
    BigInt temp = key;
    BigInt num;
    for(int i=0;i<25;i++){
        uint64_t mod = temp.divmodSmall(24);
        num.shl(8);
        num.addSmall(mod);
    }
    vector<uint8_t> numBytes = num.bytes();
    vector<uint8_t> result(25,0);
    if(numBytes.size()<=25){
        size_t offset = 25-numBytes.size();
        for(size_t i=0;i<numBytes.size();i++) result[offset+i]=numBytes[i];
    } else {
        for(int i=0;i<25;i++) result[i]=numBytes[numBytes.size()-25+i];
    }
    for(int i=0;i<12;i++) swap(result[i], result[24-i]);
    return result;
}

string to5x5(BigInt key){
    vector<uint8_t> keyBytes = encodeBytes(key);
    string key5x5;
    for(int i=1;i<25;i++) key5x5 += ALPHABET[keyBytes[i]];

    int nPos = (int)keyBytes[0];
    if(nPos > (int)key5x5.size()) nPos = (int)key5x5.size();

    string result = key5x5.substr(0,nPos) + "N" + key5x5.substr(nPos);

    return result.substr(0,5)+"-"+result.substr(5,5)+"-"+result.substr(10,5)+"-"+
           result.substr(15,5)+"-"+result.substr(20,5);
}

uint64_t checksumKey(BigInt key){
    vector<uint8_t> keyBytes = key.bytes();
    vector<uint8_t> padded(16,0);
    if(keyBytes.size()<=16){
        size_t off = 16-keyBytes.size();
        for(size_t i=0;i<keyBytes.size();i++) padded[off+i]=keyBytes[i];
    } else {
        for(int i=0;i<16;i++) padded[i]=keyBytes[keyBytes.size()-16+i];
    }
    for(int i=0;i<8;i++) swap(padded[i], padded[15-i]);

    uint32_t crc = 0xffffffffu;
    for(uint8_t b: padded){
        crc = (crc<<8) ^ CRC32_TABLE[((crc>>24)^(uint32_t)b)&0xff];
    }
    return (uint64_t)(~crc) & 0x3ffULL;
}

struct ProductKeyEncoder {
    string Key5x5;
    BigInt Key;
    uint64_t Group, Serial, Security, Checksum, Upgrade, Extra;
};

bool NewProductKeyEncoder(uint64_t group, uint64_t serial, uint64_t security,
                           uint64_t upgrade, uint64_t checksum, uint64_t extra,
                           ProductKeyEncoder& out, string& err){
    if(group>0xfffffULL || serial>0x3fffffffULL || security>0x1fffffffffffffULL ||
       checksum>0x400ULL || upgrade>0x1ULL || extra>0x1ULL){
        err = "key parameter(s) not within bounds";
        return false;
    }

    BigInt key;

    if(extra>0){
        BigInt t((uint64_t)extra);
        t.shl(114);
        for(size_t i=0;i<t.limbs.size();i++){
            if(i>=key.limbs.size()) key.limbs.push_back(0);
            key.limbs[i] |= t.limbs[i];
        }
    }
    if(upgrade>0){
        BigInt t((uint64_t)upgrade);
        t.shl(113);
        for(size_t i=0;i<t.limbs.size();i++){
            if(i>=key.limbs.size()) key.limbs.push_back(0);
            key.limbs[i] |= t.limbs[i];
        }
    }
    if(security>0){
        BigInt t((uint64_t)security);
        t.shl(50);
        for(size_t i=0;i<t.limbs.size();i++){
            if(i>=key.limbs.size()) key.limbs.push_back(0);
            key.limbs[i] |= t.limbs[i];
        }
    }
    if(serial>0){
        BigInt t((uint64_t)serial);
        t.shl(20);
        for(size_t i=0;i<t.limbs.size();i++){
            if(i>=key.limbs.size()) key.limbs.push_back(0);
            key.limbs[i] |= t.limbs[i];
        }
    }
    if(group>0){
        BigInt t((uint64_t)group);
        for(size_t i=0;i<t.limbs.size();i++){
            if(i>=key.limbs.size()) key.limbs.push_back(0);
            key.limbs[i] |= t.limbs[i];
        }
    }
    key.trim();

    if(checksum==0x400ULL) checksum = checksumKey(key);

    {
        BigInt t((uint64_t)checksum);
        t.shl(103);
        for(size_t i=0;i<t.limbs.size();i++){
            if(i>=key.limbs.size()) key.limbs.push_back(0);
            key.limbs[i] |= t.limbs[i];
        }
        key.trim();
    }

    if(extra!=0){
        BigInt maxVal = BigInt::fromHex("62A32B15518");
        maxVal.shl(72);
        if(key.cmp(maxVal) > 0){
            err = "extra parameter unencodable";
            return false;
        }
    }

    out.Key = key;
    out.Key5x5 = to5x5(key);
    out.Group = group;
    out.Serial = serial;
    out.Security = security;
    out.Checksum = checksum;
    out.Upgrade = upgrade;
    out.Extra = extra;
    return true;
}

// ---------------- CLI helpers ----------------
bool parseHexOrDec(const string& s, uint64_t& out){
    try{
        if(s.size()>2 && (s[0]=='0') && (s[1]=='x'||s[1]=='X')){
            out = stoull(s.substr(2), nullptr, 16);
        } else {
            out = stoull(s, nullptr, 10);
        }
        return true;
    } catch(...) {
        return false;
    }
}

// crude getopt-like flag extraction: removes "-flag value" or "-flag=value"
// pairs from args and returns remaining positional arguments
vector<string> extractFlags(const vector<string>& args, vector<pair<string,string>>& flagDefs){
    // flagDefs: name -> default; filled with parsed value
    vector<string> positional;
    for(size_t i=0;i<args.size();i++){
        const string& a = args[i];
        bool matched=false;
        if(a.size()>1 && a[0]=='-'){
            string name, val;
            auto eq = a.find('=');
            if(eq!=string::npos){
                name = a.substr(1, eq-1);
                val = a.substr(eq+1);
            } else {
                name = a.substr(1);
                if(i+1<args.size()){ val = args[i+1]; i++; }
            }
            for(auto& fd: flagDefs){
                if(fd.first==name){ fd.second = val; matched=true; break; }
            }
        }
        if(!matched) positional.push_back(a);
    }
    return positional;
}

int main(int argc, char* argv[]){
    initCrc();

    vector<string> args(argv+1, argv+argc);
    if(args.empty()){
        printf("Usage: keycutter [encode|decode]\n");
        return 1;
    }

    string command = args[0];
    vector<string> rest(args.begin()+1, args.end());

    if(command=="decode"){
        vector<pair<string,string>> flags = {{"output","parametric"}};
        vector<string> pos = extractFlags(rest, flags);
        string output = flags[0].second;

        if(pos.empty()){
            printf("Usage: keycutter decode [-output format] <key>\n");
            return 1;
        }
        string key = pos[0];

        string pattern = "(?:[" + ALPHABET + "N]{5}-){4}[" + ALPHABET + "N]{4}[" + ALPHABET + "]";
        std::regex re(pattern);
        if(!std::regex_search(key, re)){
            printf("Invalid product key\n");
            return 1;
        }

        ProductKeyDecoder keyi = NewProductKeyDecoder(key);

        if(output=="parametric"){
            printf("\nPKey     : [%s]\n        -> [%s]\n\n", keyi.Key5x5.c_str(), keyi.Key.toHexPadded(32).c_str());
            printf("            0xfffff\n");
            printf("Group    : [0x%05llx]\n\n", (unsigned long long)keyi.Group);
            printf("            0x3fffffff\n");
            printf("Serial   : [0x%08llx]\n\n", (unsigned long long)keyi.Serial);
            printf("            0x1FFFFFFFFFFFFF\n");
            printf("Security : [0x%014llx]\n\n", (unsigned long long)keyi.Security);
            printf("            0x3FF\n");
            printf("Checksum : [0x%03llx]\n\n", (unsigned long long)keyi.Checksum);
            printf("            0x1\n");
            printf("Upgrade  : [0x%01llx]\n\n", (unsigned long long)keyi.Upgrade);
            printf("            0x1\n");
            printf("Extra    : [0x%01llx]\n\n", (unsigned long long)keyi.Extra);
        } else if(output=="raw"){
            printf("%s\n%s\n%llu\n%llu\n%llu\n%llu\n%llu\n%llu\n",
                keyi.Key5x5.c_str(), keyi.Key.toDec().c_str(),
                (unsigned long long)keyi.Group, (unsigned long long)keyi.Serial,
                (unsigned long long)keyi.Security, (unsigned long long)keyi.Upgrade,
                (unsigned long long)keyi.Checksum, (unsigned long long)keyi.Extra);
        } else if(output=="rawhex"){
            printf("%s\n%s\n0x%llx\n0x%llx\n0x%llx\n0x%llx\n0x%llx\n0x%llx\n",
                keyi.Key5x5.c_str(), keyi.Key.toHex().c_str(),
                (unsigned long long)keyi.Group, (unsigned long long)keyi.Serial,
                (unsigned long long)keyi.Security, (unsigned long long)keyi.Upgrade,
                (unsigned long long)keyi.Checksum, (unsigned long long)keyi.Extra);
        }

    } else if(command=="encode"){
        vector<pair<string,string>> flags = {{"u","0"},{"c","0x400"},{"e","0"}};
        vector<string> pos = extractFlags(rest, flags);

        if(pos.size()<3){
            printf("Usage: keycutter encode [-u upgrade] [-c checksum] [-e extra] <group> <serial> <security>\n");
            return 1;
        }

        uint64_t group=0, serial=0, security=0, upgradeVal=0, checksumVal=0, extraVal=0;
        parseHexOrDec(pos[0], group);
        parseHexOrDec(pos[1], serial);
        parseHexOrDec(pos[2], security);
        parseHexOrDec(flags[0].second, upgradeVal);
        parseHexOrDec(flags[1].second, checksumVal);
        parseHexOrDec(flags[2].second, extraVal);

        ProductKeyEncoder keyi;
        string err;
        if(!NewProductKeyEncoder(group, serial, security, upgradeVal, checksumVal, extraVal, keyi, err)){
            printf("Error: %s\n", err.c_str());
            return 1;
        }

        printf("%s\n", keyi.Key5x5.c_str());

    } else {
        printf("Unknown command. Use: encode, decode, or template\n");
        return 1;
    }

    return 0;
}
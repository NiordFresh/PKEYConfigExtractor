package main

import (
	"flag"
	"fmt"
	"math/big"
	"os"
	"regexp"
	"strconv"
	"strings"
)

const ALPHABET = "BCDFGHJKMPQRTVWXY2346789"

var CRC32_TABLE []uint32

func init() {
	CRC32_TABLE = make([]uint32, 256)
	for i := 0; i < 256; i++ {
		k := uint32(i) << 24
		for bit := 0; bit < 8; bit++ {
			if k&0x80000000 != 0 {
				k = (k << 1) ^ 0x4c11db7
			} else {
				k = k << 1
			}
		}
		CRC32_TABLE[i] = k & 0xffffffff
	}
}

type ProductKeyDecoder struct {
	Key5x5   string
	Key      *big.Int
	Group    uint64
	Serial   uint64
	Security uint64
	Checksum uint64
	Upgrade  uint64
	Extra    uint64
}

func decode5x5(key string) *big.Int {
	key = strings.ReplaceAll(key, "-", "")
	
	dec := []int{strings.Index(key, "N")}
	keyNoN := strings.ReplaceAll(key, "N", "")
	
	for _, l := range keyNoN {
		dec = append(dec, strings.IndexRune(ALPHABET, l))
	}
	
	result := big.NewInt(0)
	twentyFour := big.NewInt(24)
	
	for _, x := range dec {
		result.Mul(result, twentyFour)
		result.Add(result, big.NewInt(int64(x)))
	}
	
	return result
}

func NewProductKeyDecoder(key string) *ProductKeyDecoder {
	keyInt := decode5x5(key)
	
	mask1 := new(big.Int)
	mask1.SetString("fffff", 16)
	
	mask2 := new(big.Int)
	mask2.SetString("3fffffff00000", 16)
	
	mask3 := new(big.Int)
	mask3.SetString("1fffffffffffff000000000000", 16)
	
	mask4 := new(big.Int)
	mask4.SetString("3ff0000000000000000000000000", 16)
	
	mask5 := new(big.Int)
	mask5.SetString("20000000000000000000000000000", 16)
	
	mask6 := new(big.Int)
	mask6.SetString("40000000000000000000000000000", 16)
	
	group := new(big.Int).And(keyInt, mask1)
	serial := new(big.Int).And(keyInt, mask2)
	serial.Rsh(serial, 20)
	security := new(big.Int).And(keyInt, mask3)
	security.Rsh(security, 50)
	checksum := new(big.Int).And(keyInt, mask4)
	checksum.Rsh(checksum, 103)
	upgrade := new(big.Int).And(keyInt, mask5)
	upgrade.Rsh(upgrade, 113)
	extra := new(big.Int).And(keyInt, mask6)
	extra.Rsh(extra, 114)
	
	return &ProductKeyDecoder{
		Key5x5:   key,
		Key:      keyInt,
		Group:    group.Uint64(),
		Serial:   serial.Uint64(),
		Security: security.Uint64(),
		Checksum: checksum.Uint64(),
		Upgrade:  upgrade.Uint64(),
		Extra:    extra.Uint64(),
	}
}

type ProductKeyEncoder struct {
	Key5x5   string
	Key      *big.Int
	Group    uint64
	Serial   uint64
	Security uint64
	Checksum uint64
	Upgrade  uint64
	Extra    uint64
}

func encode(key *big.Int) []byte {
	twentyFour := big.NewInt(24)
	temp := new(big.Int).Set(key)
	num := big.NewInt(0)
	
	for i := 0; i < 25; i++ {
		mod := new(big.Int)
		temp.DivMod(temp, twentyFour, mod)
		
		num.Lsh(num, 8)
		num.Or(num, mod)
	}

	numBytes := num.Bytes()

	result := make([]byte, 25)
	if len(numBytes) <= 25 {
		copy(result[25-len(numBytes):], numBytes)
	} else {
		copy(result, numBytes[len(numBytes)-25:])
	}
	for i := 0; i < 12; i++ {
		result[i], result[24-i] = result[24-i], result[i]
	}
	
	return result
}

func to5x5(key *big.Int) string {
	keyBytes := encode(key)
	key5x5 := make([]byte, 0, 24)
	for i := 1; i < 25; i++ {
		key5x5 = append(key5x5, ALPHABET[keyBytes[i]])
	}
	nPos := int(keyBytes[0])
	if nPos > len(key5x5) {
		nPos = len(key5x5)
	}
	result := make([]byte, 0, 25)
	result = append(result, key5x5[:nPos]...)
	result = append(result, 'N')
	result = append(result, key5x5[nPos:]...)
	
	final := string(result[:5]) + "-" + string(result[5:10]) + "-" + 
	         string(result[10:15]) + "-" + string(result[15:20]) + "-" + 
	         string(result[20:])
	
	return final
}

func checksumKey(key *big.Int) uint64 {

	keyBytes := key.Bytes()
	
	padded := make([]byte, 16)
	if len(keyBytes) <= 16 {
		copy(padded[16-len(keyBytes):], keyBytes)
	} else {
		copy(padded, keyBytes[len(keyBytes)-16:])
	}
	

	for i := 0; i < 8; i++ {
		padded[i], padded[15-i] = padded[15-i], padded[i]
	}
	
	crc := uint32(0xffffffff)
	for _, b := range padded {
		crc = (crc << 8) ^ CRC32_TABLE[((crc>>24)^uint32(b))&0xff]
	}
	
	return uint64(^crc & 0x3ff)
}

func NewProductKeyEncoder(group, serial, security, upgrade, checksum, extra uint64) (*ProductKeyEncoder, error) {

	if group > 0xfffff || serial > 0x3fffffff || security > 0x1fffffffffffff ||
		checksum > 0x400 || upgrade > 0x1 || extra > 0x1 {
		return nil, fmt.Errorf("key parameter(s) not within bounds")
	}
	key := big.NewInt(0)
	
	if extra > 0 {
		temp := big.NewInt(int64(extra))
		temp.Lsh(temp, 114)
		key.Or(key, temp)
	}
	
	if upgrade > 0 {
		temp := big.NewInt(int64(upgrade))
		temp.Lsh(temp, 113)
		key.Or(key, temp)
	}
	
	if security > 0 {
		temp := big.NewInt(int64(security))
		temp.Lsh(temp, 50)
		key.Or(key, temp)
	}
	
	if serial > 0 {
		temp := big.NewInt(int64(serial))
		temp.Lsh(temp, 20)
		key.Or(key, temp)
	}
	
	if group > 0 {
		temp := big.NewInt(int64(group))
		key.Or(key, temp)
	}
	
	if checksum == 0x400 {
		checksum = checksumKey(key)
	}
	
	temp := big.NewInt(int64(checksum))
	temp.Lsh(temp, 103)
	key.Or(key, temp)
	
	if extra != 0 {
		maxVal := new(big.Int)
		maxVal.SetString("62A32B15518", 16)
		maxVal.Lsh(maxVal, 72)
		if key.Cmp(maxVal) > 0 {
			return nil, fmt.Errorf("extra parameter unencodable")
		}
	}
	
	return &ProductKeyEncoder{
		Key:      key,
		Key5x5:   to5x5(key),
		Group:    group,
		Serial:   serial,
		Security: security,
		Checksum: checksum,
		Upgrade:  upgrade,
		Extra:    extra,
	}, nil
}

func parseHexOrDec(s string) (uint64, error) {
	if strings.HasPrefix(s, "0x") || strings.HasPrefix(s, "0X") {
		return strconv.ParseUint(s[2:], 16, 64)
	}
	return strconv.ParseUint(s, 0, 64)
}

func main() {
	if len(os.Args) < 2 {
		fmt.Println("Usage: keycutter [encode|decode]")
		os.Exit(1)
	}
	
	command := os.Args[1]
	
	switch command {
	case "decode":
		decodeCmd := flag.NewFlagSet("decode", flag.ExitOnError)
		output := decodeCmd.String("output", "parametric", "Output format: parametric, raw, rawhex")
		decodeCmd.Parse(os.Args[2:])
		
		if decodeCmd.NArg() < 1 {
			fmt.Println("Usage: keycutter decode [-output format] <key>")
			os.Exit(1)
		}
		
		key := decodeCmd.Arg(0)
		

		alphaPattern := fmt.Sprintf("(?:[%sN]{5}-){4}[%sN]{4}[%s]", ALPHABET, ALPHABET, ALPHABET)
		matched, _ := regexp.MatchString(alphaPattern, key)
		if !matched {
			fmt.Println("Invalid product key")
			os.Exit(1)
		}
		
		keyi := NewProductKeyDecoder(key)
		
		switch *output {
		case "parametric":
			fmt.Printf("\nPKey     : [%s]\n        -> [%032x]\n\n", keyi.Key5x5, keyi.Key)
			fmt.Println("            0xfffff")
			fmt.Printf("Group    : [0x%05x]\n\n", keyi.Group)
			fmt.Println("            0x3fffffff")
			fmt.Printf("Serial   : [0x%08x]\n\n", keyi.Serial)
			fmt.Println("            0x1FFFFFFFFFFFFF")
			fmt.Printf("Security : [0x%014x]\n\n", keyi.Security)
			fmt.Println("            0x3FF")
			fmt.Printf("Checksum : [0x%03x]\n\n", keyi.Checksum)
			fmt.Println("            0x1")
			fmt.Printf("Upgrade  : [0x%01x]\n\n", keyi.Upgrade)
			fmt.Println("            0x1")
			fmt.Printf("Extra    : [0x%01x]\n\n", keyi.Extra)
		case "raw":
			fmt.Printf("%s\n%s\n%d\n%d\n%d\n%d\n%d\n%d\n",
				keyi.Key5x5, keyi.Key.String(),
				keyi.Group, keyi.Serial, keyi.Security,
				keyi.Upgrade, keyi.Checksum, keyi.Extra)
		case "rawhex":
			fmt.Printf("%s\n%s\n0x%x\n0x%x\n0x%x\n0x%x\n0x%x\n0x%x\n",
				keyi.Key5x5, keyi.Key.Text(16),
				keyi.Group, keyi.Serial, keyi.Security,
				keyi.Upgrade, keyi.Checksum, keyi.Extra)
		}
		
	case "encode":
		encodeCmd := flag.NewFlagSet("encode", flag.ExitOnError)
		upgrade := encodeCmd.String("u", "0", "Upgrade bit")
		checksum := encodeCmd.String("c", "0x400", "Checksum (0x400 for automatic)")
		extra := encodeCmd.String("e", "0", "Extra bit")
		encodeCmd.Parse(os.Args[2:])
		
		if encodeCmd.NArg() < 3 {
			fmt.Println("Usage: keycutter encode [-u upgrade] [-c checksum] [-e extra] <group> <serial> <security>")
			os.Exit(1)
		}
		
		group, _ := parseHexOrDec(encodeCmd.Arg(0))
		serial, _ := parseHexOrDec(encodeCmd.Arg(1))
		security, _ := parseHexOrDec(encodeCmd.Arg(2))
		upgradeVal, _ := parseHexOrDec(*upgrade)
		checksumVal, _ := parseHexOrDec(*checksum)
		extraVal, _ := parseHexOrDec(*extra)
		
		keyi, err := NewProductKeyEncoder(group, serial, security, upgradeVal, checksumVal, extraVal)
		if err != nil {
			fmt.Println("Error:", err)
			os.Exit(1)
		}
		
		fmt.Println(keyi.Key5x5)
		
	default:
		fmt.Println("Unknown command. Use: encode, decode, or template")
		os.Exit(1)
	}
}
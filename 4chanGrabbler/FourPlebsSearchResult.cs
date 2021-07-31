namespace FourPlebsSearchResult
{


    public class RootobjectErrored
    {
        public string error { get; set; }
    }


    public class Rootobject
    {
        public _0 root { get; set; }
        public Meta meta { get; set; }
    }

    public class _0
    {
        public Post[] posts { get; set; }
    }

    public class Post
    {
        public string doc_id { get; set; }
        public string num { get; set; }
        public string subnum { get; set; }
        public string thread_num { get; set; }
        public string op { get; set; }
        public int timestamp { get; set; }
        public int timestamp_expired { get; set; }
        public string capcode { get; set; }
        public object email { get; set; }
        public string name { get; set; }
        public object trip { get; set; }
        public string title { get; set; }
        public string comment { get; set; }
        public string poster_hash { get; set; }
        public string poster_country { get; set; }
        public string sticky { get; set; }
        public string locked { get; set; }
        public string deleted { get; set; }
        public int? nreplies { get; set; }
        public object nimages { get; set; }
        public string fourchan_date { get; set; }
        public string comment_sanitized { get; set; }
        public string comment_processed { get; set; }
        public bool formatted { get; set; }
        public string title_processed { get; set; }
        public string name_processed { get; set; }
        public object email_processed { get; set; }
        public object trip_processed { get; set; }
        public string poster_hash_processed { get; set; }
        public object poster_country_name { get; set; }
        public string poster_country_name_processed { get; set; }
        public string exif { get; set; }
        public object troll_country_code { get; set; }
        public string troll_country_name { get; set; }
        public object since4pass { get; set; }
        public string unique_ips { get; set; }
        public Extra_Data extra_data { get; set; }
        public Media media { get; set; }
        public Board board { get; set; }
    }

    public class Extra_Data
    {
        public object since4pass { get; set; }
        public string uniqueIps { get; set; }
    }

    public class Media
    {
        public string media_id { get; set; }
        public string spoiler { get; set; }
        public string preview_orig { get; set; }
        public string media { get; set; }
        public string preview_op { get; set; }
        public string preview_reply { get; set; }
        public int preview_w { get; set; }
        public int preview_h { get; set; }
        public string media_filename { get; set; }
        public string media_w { get; set; }
        public string media_h { get; set; }
        public string media_size { get; set; }
        public string media_hash { get; set; }
        public string media_orig { get; set; }
        public string exif { get; set; }
        public string total { get; set; }
        public string banned { get; set; }
        public string media_status { get; set; }
        public string safe_media_hash { get; set; }
        public string remote_media_link { get; set; }
        public string media_link { get; set; }
        public string thumb_link { get; set; }
        public string media_filename_processed { get; set; }
    }

    public class Board
    {
        public string name { get; set; }
        public string shortname { get; set; }
    }

    public class Meta
    {
        public int total_found { get; set; }
        public string max_results { get; set; }
        public string search_title { get; set; }
    }
}